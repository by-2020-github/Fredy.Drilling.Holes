# Copilot Instructions

## 项目指南
- User prefers using CommunityToolkit.Mvvm helpers such as ObservableObject and RelayCommand instead of custom MVVM helper classes in this codebase.
- User prefers GPIO and XYZ position updates to be refreshed in the background, with UI/business layers subscribing to state changes rather than polling directly in the ViewModel.

## 记忆
在此代码库中，用户偏好使用 CommunityToolkit.Mvvm 辅助类，并且 GPIO 与 XYZ 位置更新倾向于后台刷新后由 UI/业务层订阅状态变化，而不是在 ViewModel 中直接轮询。