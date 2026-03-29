// Resolve ambiguity between WPF and WinForms types.
// Default to WPF types since this is a WPF app.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
