using Common.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Common.Tools
{
    public class RecipeService
    {
        private const string RecipeFileName = "recipe.json";
        private static readonly JsonSerializerOptions JsonSerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        private readonly object _syncRoot = new();
        private readonly Task _initializationTask;
        private Dictionary<string, Recipe> _recipes = new(StringComparer.OrdinalIgnoreCase);

        public RecipeService()
        {
            _initializationTask = LoadAllRecipesAsync();
        }

        public string RuntimePath => PathManager.RecipePath;

        public Recipe? CurrentRecipe { get; private set; }

        public IReadOnlyDictionary<string, Recipe> Recipes
        {
            get
            {
                EnsureInitialized();

                lock (_syncRoot)
                {
                    return new Dictionary<string, Recipe>(_recipes, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        /*
         recipe,实体类型：Common.Models.Recipe， 存储路径如下:
        RuntimePath
            recipeName
                recipe.json
         */

        public bool Save(string type, object recipe)
        {
            EnsureInitialized();

            if (!TryValidateRecipe(type, recipe, out var recipeModel))
            {
                return false;
            }

            lock (_syncRoot)
            {
                // if (_recipes.ContainsKey(type) || HasDuplicateTypeName(recipeModel.TypeName))
                if (_recipes.ContainsKey(type))
                {
                    return false;
                }

                var recipeDirectory = GetRecipeDirectory(type);
                Directory.CreateDirectory(recipeDirectory);
                File.WriteAllText(GetRecipeFilePath(type), JsonSerializer.Serialize(recipeModel, JsonSerializerOptions));
                _recipes[type] = recipeModel;
                CurrentRecipe = recipeModel;
                return true;
            }
        }

        public bool Update(string type, object recipe)
        {
            EnsureInitialized();

            if (!TryValidateRecipe(type, recipe, out var recipeModel))
            {
                return false;
            }

            lock (_syncRoot)
            {
                // if (!_recipes.ContainsKey(type) || HasDuplicateTypeName(recipeModel.TypeName, type))
                if (!_recipes.ContainsKey(type))
                {
                    return false;
                }

                var recipeFilePath = GetRecipeFilePath(type);
                Directory.CreateDirectory(GetRecipeDirectory(type));
                File.WriteAllText(recipeFilePath, JsonSerializer.Serialize(recipeModel, JsonSerializerOptions));
                _recipes[type] = recipeModel;
                CurrentRecipe = recipeModel;
                return true;
            }
        }

        public bool Load(string type)
        {
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(type))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(RuntimePath))
            {
                return false;
            }

            lock (_syncRoot)
            {
                if (!_recipes.TryGetValue(type, out var recipe))
                {
                    return false;
                }

                CurrentRecipe = recipe;
                return true;
            }
        }

        public bool Delete(string type)
        {
            EnsureInitialized();

            if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(RuntimePath))
            {
                return false;
            }

            lock (_syncRoot)
            {
                if (!_recipes.Remove(type, out var deletedRecipe))
                {
                    return false;
                }

                var recipeDirectory = GetRecipeDirectory(type);
                if (Directory.Exists(recipeDirectory))
                {
                    Directory.Delete(recipeDirectory, true);
                }

                if (CurrentRecipe == deletedRecipe)
                {
                    CurrentRecipe = null;
                }

                return true;
            }
        }

        private string GetRecipeDirectory(string type)
        {
            return Path.Combine(RuntimePath, type);
        }

        private string GetRecipeFilePath(string type)
        {
            return Path.Combine(GetRecipeDirectory(type), RecipeFileName);
        }

        private bool TryValidateRecipe(string type, object recipe, out Recipe recipeModel)
        {
            recipeModel = null!;

            if (string.IsNullOrWhiteSpace(RuntimePath)
                || string.IsNullOrWhiteSpace(type)
                || recipe is not Recipe model
                || string.IsNullOrWhiteSpace(model.TypeName))
            {
                return false;
            }

            Directory.CreateDirectory(RuntimePath);
            recipeModel = model;
            return true;
        }

        private bool HasDuplicateTypeName(string typeName, string? excludedRecipeName = null)
        {
            return _recipes.Any(x => string.Equals(x.Value.TypeName, typeName, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(x.Key, excludedRecipeName, StringComparison.OrdinalIgnoreCase));
        }

        private void EnsureInitialized()
        {
            _initializationTask.GetAwaiter().GetResult();
        }

        private async Task LoadAllRecipesAsync()
        {
            if (string.IsNullOrWhiteSpace(RuntimePath))
            {
                return;
            }

            Directory.CreateDirectory(RuntimePath);

            var directories = Directory.GetDirectories(RuntimePath);
            var loadTasks = directories.Select(LoadRecipeAsync).ToArray();
            var loadedRecipes = await Task.WhenAll(loadTasks).ConfigureAwait(false);

            var recipeMap = new Dictionary<string, Recipe>(StringComparer.OrdinalIgnoreCase);
            var typeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var loadedRecipe in loadedRecipes
                         .Where(x => x.HasValue)
                         .Select(x => x!.Value)
                         .OrderBy(x => x.RecipeName, StringComparer.OrdinalIgnoreCase))
            {
                // if (!typeNames.Add(loadedRecipe.Recipe.TypeName))
                // {
                //     continue;
                // }

                typeNames.Add(loadedRecipe.Recipe.TypeName);
                Debug.WriteLine(loadedRecipe.RecipeName);
                recipeMap[loadedRecipe.RecipeName] = loadedRecipe.Recipe;
            }

            lock (_syncRoot)
            {
                _recipes = recipeMap;

                // --- 新增逻辑：如果为空，则初始化虚拟数据 ---
                if (_recipes.Count == 0)
                {
                    InitializeDefaultRecipes();
                }
            }
        }

        private async Task<(string RecipeName, Recipe Recipe)?> LoadRecipeAsync(string recipeDirectory)
        {
            var recipeName = Path.GetFileName(recipeDirectory);
            var recipeFilePath = Path.Combine(recipeDirectory, RecipeFileName);
            if (!File.Exists(recipeFilePath))
            {
                return null;
            }

            try
            {
                var json = await File.ReadAllTextAsync(recipeFilePath).ConfigureAwait(false);
                var recipe = JsonSerializer.Deserialize<Recipe>(json, JsonSerializerOptions);
                if (recipe is null || string.IsNullOrWhiteSpace(recipe.TypeName))
                {
                    return null;
                }

                return (recipeName, recipe);
            }
            catch
            {
                return null;
            }
        }


        #region 创建虚拟recipe
        private void InitializeDefaultRecipes()
        {
            for (int i = 1; i <= 10; i++)
            {
                string name = $"Virtual_Recipe_{i:D2}";

                var rings = (new Random()).Next(3, 10);
                var radius = (new Random()).Next(100, 200);
                var space = radius / rings;
                // 调用我们之前在 Recipe 类中定义的静态工厂方法
                // 这里的参数可以根据需要调整，比如让每一层的孔数递增
                var virtualRecipe = Recipe.CreateVirtualRingRecipe(
                    recipeName: name,
                    rings: rings,
                    pointsPerRing: 8 + i,
                    spacing: space,
                    radius: radius
                );

                // 这里的 type 传入 name，保持目录名与 RecipeName 一致
                // 注意：因为 Save 内部有 lock，而 InitializeDefaultRecipes 
                // 已经在 LoadAllRecipesAsync 的 lock 块中，
                // 如果 Save 内部也用了 lock(_syncRoot)，请确保使用可重入锁或直接写文件。
                // 按照你提供的代码，Save 是线程安全的，可以直接调用。
                InternalSave(name, virtualRecipe);
            }
        }

        /// <summary>
        /// 内部专用的保存方法，规避外部 Save 方法可能存在的初始化检查竞争
        /// </summary>
        private void InternalSave(string type, Recipe recipeModel)
        {
            var recipeDirectory = GetRecipeDirectory(type);
            Directory.CreateDirectory(recipeDirectory);

            var json = JsonSerializer.Serialize(recipeModel, JsonSerializerOptions);
            File.WriteAllText(GetRecipeFilePath(type), json);

            _recipes[type] = recipeModel;
        }
        #endregion
    }



}
