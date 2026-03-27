# Copilot Instructions

## 项目指南
- User prefers using CommunityToolkit.Mvvm helpers such as ObservableObject and RelayCommand instead of custom MVVM helper classes in this codebase.
- User prefers GPIO and XYZ position updates to be refreshed in the background, with UI/business layers subscribing to state changes rather than polling directly in the ViewModel.