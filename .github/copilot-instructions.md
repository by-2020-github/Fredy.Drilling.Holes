# Copilot Instructions

## 项目指南
- User prefers using CommunityToolkit.Mvvm helpers such as ObservableObject and RelayCommand instead of custom MVVM helper classes in this codebase.
- User prefers GPIO and XYZ position updates to be refreshed in the background, with UI/business layers subscribing to state changes rather than polling directly in the ViewModel.
- Use HandyControl NumericUpDown for all numeric input fields in windows; configure int and double fields with different DecimalPlaces and Increment values, where double fields should keep 3 decimal places。
- For the Hikvision camera trigger mode, detach image callbacks before synchronous trigger-frame acquisition and reattach them only when entering continuous grab mode。

## 记忆
在此代码库中，用户偏好使用 CommunityToolkit.Mvvm 辅助类，并且 GPIO 与 XYZ 位置更新倾向于后台刷新后由 UI/业务层订阅状态变化，而不是在 ViewModel 中直接轮询。
在此代码库中，所有数字输入框统一使用 HandyControl 的 NumericUpDown；int 和 double 通过 DecimalPlaces 与 Increment 分别限制，其中 double 保留 3 位小数。
针对海康威视相机的触发模式，在同步触发帧采集之前应分离图像回调，仅在进入连续抓取模式时重新连接。