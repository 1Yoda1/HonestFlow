# HonestFlow UI architecture

HonestFlow 3.0 is a WPF application. Its production startup path is:

`HonestFlow.WpfPrototype/App.xaml` → `StartupWindow.xaml` → `MainWindow`.

`HonestFlow.WpfPrototype` is a historical project name; it is the current
production WPF project and must not be renamed without an explicit task.

The WinForms project and `Program.cs` startup remain legacy/fallback code.
Do not add new HonestFlow 3.0 user-facing features to WinForms unless the user
explicitly requests a legacy implementation.

New UI features belong in the WPF startup and main-window flow. Shared
application and infrastructure code should remain UI-independent and must not
be duplicated separately for WPF and WinForms.
