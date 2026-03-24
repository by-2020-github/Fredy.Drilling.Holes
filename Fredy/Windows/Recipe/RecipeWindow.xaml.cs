using Common.Models;
using Common.Tools;
using Fredy.Drilling.Holes.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;

namespace Fredy.Drilling.Holes.Windows.Recipe
{
    /// <summary>
    /// RecipeWindow.xaml 的交互逻辑
    /// </summary>
    public partial class RecipeWindow : Window, INotifyPropertyChanged
    {
        private static readonly JsonSerializerOptions CloneSerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private readonly RecipeService _recipeService;
        private RecipeViewModel? _currentRecipeViewModel;
        private string? _selectedRecipeName;
        private bool _isEditing;
        private string? _editingOriginalRecipeName;
        private string? _editSnapshotJson;

        public RecipeWindow()
        {
            InitializeComponent();
            DataContext = this;
            _recipeService = App.ServiceProvider.GetRequiredService<RecipeService>();
            RefreshRecipeNames();
            Closing += RecipeWindow_Closing;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<string> RecipeNames { get; } = new();

        public RecipeViewModel? CurrentRecipeViewModel
        {
            get => _currentRecipeViewModel;
            private set
            {
                if (SetProperty(ref _currentRecipeViewModel, value))
                {
                    OnPropertyChanged(nameof(EditingRecipeDisplayName));
                }
            }
        }

        public string? SelectedRecipeName
        {
            get => _selectedRecipeName;
            set
            {
                if (SetProperty(ref _selectedRecipeName, value) && !IsEditing)
                {
                    LoadSelectedRecipePreview();
                }
            }
        }

        public bool IsEditing
        {
            get => _isEditing;
            private set
            {
                if (SetProperty(ref _isEditing, value))
                {
                    OnPropertyChanged(nameof(CanEdit));
                    OnPropertyChanged(nameof(CanDelete));
                    OnPropertyChanged(nameof(CanCopy));
                    OnPropertyChanged(nameof(CanCreateNew));
                    OnPropertyChanged(nameof(EditingRecipeDisplayName));
                }
            }
        }

        public bool CanEdit => !IsEditing && CurrentRecipeViewModel is not null;

        public bool CanDelete => !IsEditing && !string.IsNullOrWhiteSpace(SelectedRecipeName);

        public bool CanCopy => !IsEditing && CurrentRecipeViewModel is not null;

        public bool CanCreateNew => !IsEditing;

        public string EditingRecipeDisplayName => CurrentRecipeViewModel is null
            ? "未加载 Recipe"
            : IsEditing
                ? $"编辑中: {CurrentRecipeViewModel.RecipeName}"
                : $"当前预览: {CurrentRecipeViewModel.RecipeName}";

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshRecipeNames(SelectedRecipeName);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentRecipeViewModel is null)
            {
                LoadSelectedRecipePreview();
            }

            if (CurrentRecipeViewModel is null)
            {
                MessageBox.Show("请先选择一个 Recipe。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BeginEdit(CurrentRecipeViewModel.Recipe, _editingOriginalRecipeName ?? SelectedRecipeName);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!IsEditing || CurrentRecipeViewModel is null)
            {
                MessageBox.Show("请先加载或新建一个 Recipe。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var recipe = CloneRecipe(CurrentRecipeViewModel.Recipe);
            recipe.RecipeName = recipe.RecipeName.Trim();
            recipe.TypeName = recipe.TypeName.Trim();

            if (string.IsNullOrWhiteSpace(recipe.RecipeName))
            {
                MessageBox.Show("RecipeName 不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(recipe.TypeName))
            {
                MessageBox.Show("TypeName 不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool success;
            if (string.IsNullOrWhiteSpace(_editingOriginalRecipeName))
            {
                success = _recipeService.Save(recipe.RecipeName, recipe);
            }
            else if (string.Equals(_editingOriginalRecipeName, recipe.RecipeName, StringComparison.OrdinalIgnoreCase))
            {
                success = _recipeService.Update(_editingOriginalRecipeName, recipe);
            }
            else
            {
                if (_recipeService.Recipes.ContainsKey(recipe.RecipeName))
                {
                    MessageBox.Show($"Recipe [{recipe.RecipeName}] 已存在。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                success = _recipeService.Save(recipe.RecipeName, recipe);
                if (success)
                {
                    _recipeService.Delete(_editingOriginalRecipeName);
                }
            }

            if (!success)
            {
                MessageBox.Show("保存失败，请检查 RecipeName 是否重复。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _editingOriginalRecipeName = recipe.RecipeName;
            RefreshRecipeNames(recipe.RecipeName);
            LoadRecipeToEditor(recipe.RecipeName, isEditing: false);
            MessageBox.Show("Recipe 保存成功。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelEdit();
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(SelectedRecipeName))
            {
                MessageBox.Show("请先选择要删除的 Recipe。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show($"确认删除 Recipe [{SelectedRecipeName}] ?", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            if (!_recipeService.Delete(SelectedRecipeName))
            {
                MessageBox.Show("删除失败。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.Equals(_editingOriginalRecipeName, SelectedRecipeName, StringComparison.OrdinalIgnoreCase))
            {
                CurrentRecipeViewModel = null;
                _editingOriginalRecipeName = null;
            }

            RefreshRecipeNames();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var sourceRecipe = CurrentRecipeViewModel?.Recipe;

            if (sourceRecipe is null)
            {
                if (string.IsNullOrWhiteSpace(SelectedRecipeName) || !_recipeService.Recipes.TryGetValue(SelectedRecipeName, out var selectedRecipe))
                {
                    MessageBox.Show("请先选择或加载一个 Recipe。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                sourceRecipe = selectedRecipe;
            }

            var copiedRecipe = CloneRecipe(sourceRecipe);
            copiedRecipe.RecipeName = GenerateUniqueRecipeName($"{copiedRecipe.RecipeName}_Copy");
            BeginEdit(copiedRecipe, null);
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            var recipeName = GenerateUniqueRecipeName("New_Recipe");
            var recipe = new Common.Models.Recipe
            {
                RecipeName = recipeName,
                TypeName = "Custom",
                PunchParameters = RecipePunchParameters.CreateDefault(),
                ProcessParameters = RecipeProcessParameters.CreateDefault(recipeName)
            };

            BeginEdit(recipe, null);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryHandlePendingEdit())
            {
                return;
            }

            Close();
        }

        private void LoadSelectedRecipePreview()
        {
            if (IsEditing || string.IsNullOrWhiteSpace(SelectedRecipeName))
            {
                if (string.IsNullOrWhiteSpace(SelectedRecipeName))
                {
                    CurrentRecipeViewModel = null;
                    _editingOriginalRecipeName = null;
                }

                return;
            }

            LoadRecipeToEditor(SelectedRecipeName, isEditing: false);
        }

        private void LoadRecipeToEditor(string? recipeName, bool isEditing)
        {
            if (string.IsNullOrWhiteSpace(recipeName))
            {
                CurrentRecipeViewModel = null;
                _editingOriginalRecipeName = null;
                _editSnapshotJson = null;
                IsEditing = false;
                return;
            }

            if (!_recipeService.Recipes.TryGetValue(recipeName, out var recipe))
            {
                MessageBox.Show($"未找到 Recipe [{recipeName}]。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                RefreshRecipeNames();
                return;
            }

            var clonedRecipe = CloneRecipe(recipe);
            CurrentRecipeViewModel = new RecipeViewModel(clonedRecipe);
            _editingOriginalRecipeName = recipeName;
            _editSnapshotJson = SerializeRecipe(clonedRecipe);
            IsEditing = isEditing;
        }

        private void BeginEdit(Common.Models.Recipe recipe, string? originalRecipeName)
        {
            var clonedRecipe = CloneRecipe(recipe);
            CurrentRecipeViewModel = new RecipeViewModel(clonedRecipe);
            _editingOriginalRecipeName = originalRecipeName;
            _editSnapshotJson = SerializeRecipe(clonedRecipe);
            IsEditing = true;
        }

        private void CancelEdit()
        {
            if (!TryHandlePendingEdit())
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_editingOriginalRecipeName))
            {
                LoadRecipeToEditor(_editingOriginalRecipeName, isEditing: false);
                SelectedRecipeName = _editingOriginalRecipeName;
                return;
            }

            if (!string.IsNullOrWhiteSpace(SelectedRecipeName) && _recipeService.Recipes.ContainsKey(SelectedRecipeName))
            {
                LoadRecipeToEditor(SelectedRecipeName, isEditing: false);
                return;
            }

            CurrentRecipeViewModel = null;
            _editingOriginalRecipeName = null;
            _editSnapshotJson = null;
            IsEditing = false;
        }

        private void RefreshRecipeNames(string? recipeNameToSelect = null)
        {
            RecipeNames.Clear();

            foreach (var recipeName in _recipeService.Recipes.Values
                         .Select(x => x.RecipeName)
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                RecipeNames.Add(recipeName);
            }

            SelectedRecipeName = recipeNameToSelect is not null && RecipeNames.Contains(recipeNameToSelect)
                ? recipeNameToSelect
                : RecipeNames.FirstOrDefault();

            if (!IsEditing && !string.IsNullOrWhiteSpace(SelectedRecipeName))
            {
                LoadRecipeToEditor(SelectedRecipeName, isEditing: false);
            }
        }

        private string GenerateUniqueRecipeName(string baseName)
        {
            var candidate = baseName;
            var index = 1;

            while (_recipeService.Recipes.ContainsKey(candidate))
            {
                candidate = $"{baseName}_{index++}";
            }

            return candidate;
        }

        private static Common.Models.Recipe CloneRecipe(Common.Models.Recipe recipe)
        {
            var json = JsonSerializer.Serialize(recipe, CloneSerializerOptions);
            return JsonSerializer.Deserialize<Common.Models.Recipe>(json, CloneSerializerOptions)
                ?? throw new InvalidOperationException("Recipe 克隆失败。");
        }

        private string? SerializeRecipe(Common.Models.Recipe? recipe)
        {
            return recipe is null ? null : JsonSerializer.Serialize(recipe, CloneSerializerOptions);
        }

        private bool HasUnsavedChanges()
        {
            return IsEditing
                && CurrentRecipeViewModel is not null
                && !string.Equals(SerializeRecipe(CurrentRecipeViewModel.Recipe), _editSnapshotJson, StringComparison.Ordinal);
        }

        private bool TryHandlePendingEdit()
        {
            if (!IsEditing)
            {
                return true;
            }

            if (!HasUnsavedChanges())
            {
                IsEditing = false;
                return true;
            }

            var result = MessageBox.Show("当前 Recipe 有未保存修改，确认放弃修改并退出编辑状态吗？", "确认取消编辑", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                return false;
            }

            IsEditing = false;
            return true;
        }

        private void RecipeWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (TryHandlePendingEdit())
            {
                return;
            }

            e.Cancel = true;
        }

        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);

            if (propertyName == nameof(CurrentRecipeViewModel))
            {
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(CanCopy));
                OnPropertyChanged(nameof(EditingRecipeDisplayName));
            }

            if (propertyName == nameof(SelectedRecipeName))
            {
                OnPropertyChanged(nameof(CanDelete));
            }

            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
