#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using Flow.Launcher.Plugin.DirQuickJump.FileManager;
using Flow.Launcher.Plugin.DirQuickJump.Settings;

namespace Flow.Launcher.Plugin.DirQuickJump;

public class DirQuickJump : IPlugin, IContextMenu, ISettingProvider
{
    private PluginInitContext? _context;
    private Settings.Settings? _settings;

    public void Init(PluginInitContext context)
    {
        _context = context;
        _settings = context.API.LoadSettingJsonStorage<Settings.Settings>();
    }

    public List<Result> Query(Query query)
    {
        if (_context is null) throw new UnreachableException(); // Guaranteed by the caller

        List<IFileManager> fileManagers = [
            new Explorer(),
            new DirectoryOpus(),
            new XYplorer(),
            new OneCommander(),
        ];

        var entries = fileManagers
            .SelectMany(manager => manager.GetEntries())
            .Where(e =>
                query.Search.Trim() == "" ||
                _context.API.FuzzySearch(query.Search, e.Name).Success ||
                _context.API.FuzzySearch(query.Search, e.Path).Success
            );

        if (query.Search.Trim() != "")
        {
            string literalPath = query.Search;
            entries = entries.Append(new Entry(literalPath, literalPath));
        }

        // https://github.com/Flow-Launcher/Flow.Launcher/discussions/2993
        const int step = 10000;
        return entries.Select((entry, i) =>
        {
            (string name, string path) = entry;
            return new Result
            {
                Title = name,
                SubTitle = path,
                IcoPath = "icon.png",
                Action = ctx => ctx.SpecialKeyState.CtrlPressed switch
                {
                    true => CopyAction(path),
                    _ => JumpAction(path),
                },
                ContextData = path,
                Score = int.MaxValue - i * step,
            };
        }).ToList();
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        var path = (string)selectedResult.ContextData;
        var jumpAction = new Result
        {
            Title = "Jump",
            SubTitle = path,
            IcoPath = "icon.png",
            Action = _ => JumpAction(path),
        };
        var showAction = new Result
        {
            Title = "Show the path",
            IcoPath = "icon.png",
            Action = _ => ShowAction(path),
        };
        var copyAction = new Result
        {
            Title = "Copy the path",
            IcoPath = "icon.png",
            Action = _ => CopyAction(path),
        };
        return [jumpAction, showAction, copyAction];
    }

    private bool ShowAction(string text)
    {
        _context?.API.ShowMsg(text);
        return false;
    }

    private bool CopyAction(string text)
    {
        _context?.API.CopyToClipboard(text);
        return true;
    }

    private bool JumpAction(string path)
    {
        _context?.API.LogInfo("DirQuickJump", $"Jumping to {path}");
        var t = new Thread(() =>
        {
            // Jump after flow launcher window vanished (after JumpAction returned true)
            // and the dialog had been in the foreground. The class name of a dialog window is "#32770".
            bool timeOut = !SpinWait.SpinUntil(() => GetForegroundWindowClassName() == "#32770", 1000);
            if (timeOut)
            {
                _context?.API.LogWarn("DirQuickJump", "Dialog window not found");
                return;
            }

            ;
            // Assume that the dialog is in the foreground now
            DirJump(path, PInvoke.GetForegroundWindow());
        });
        t.Start();
        return true;

        static string? GetForegroundWindowClassName()
        {
            var handle = PInvoke.GetForegroundWindow();
            return Utils.GetClassName(handle);
        }
    }

    private void DirJump(string path, HWND dialogHandle)
    {
        // Alt-D or Ctrl-L to focus on the path input box
        var inputSimulator = new WindowsInput.InputSimulator();
        Action action = _settings?.Strategy switch
        {
            Strategy.AltD => () => inputSimulator.Keyboard.ModifiedKeyStroke(WindowsInput.VirtualKeyCode.LMENU,
                WindowsInput.VirtualKeyCode.VK_D),
            Strategy.CtrlL => () => inputSimulator.Keyboard.ModifiedKeyStroke(WindowsInput.VirtualKeyCode.LCONTROL,
                WindowsInput.VirtualKeyCode.VK_L),
            _ => () => throw new UnreachableException(),
        };
        action();

        // Get the handle of the path input box and then set the text.
        // The window with class name "ComboBoxEx32" is not visible when the path input box is not with the keyboard focus.
        var controlHandle = PInvoke.FindWindowEx(dialogHandle, HWND.Null, "WorkerW", null);
        controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "ReBarWindow32", null);
        controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "Address Band Root", null);
        controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "msctls_progress32", null);
        controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "ComboBoxEx32", null);
        if (controlHandle == HWND.Null)
        {
            _context?.API.LogWarn("DirQuickJump", "ComboBoxEx32 not found. Maybe a legacy dialog?");
            DirJumpOnLegacyDialog(path, dialogHandle);
            return;
        }

        bool timeOut = !SpinWait.SpinUntil(() =>
        {
            int style = PInvoke.GetWindowLong(controlHandle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
            return (style & (int)WINDOW_STYLE.WS_VISIBLE) != 0;
        }, 1000);
        if (timeOut)
        {
            _context?.API.LogWarn("DirQuickJump", $"Alt-D or Ctrl-L failed. ComboBoxEx32 handle: {controlHandle}");
            return;
        }

        var editHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "ComboBox", null);
        editHandle = PInvoke.FindWindowEx(editHandle, HWND.Null, "Edit", null);
        if (editHandle == HWND.Null)
        {
            _context?.API.LogWarn("DirQuickJump", "Edit control at address bar not found");
            return;
        }

        Utils.SetWindowText(editHandle, path);
        inputSimulator.Keyboard.KeyPress(WindowsInput.VirtualKeyCode.RETURN);
    }

    private void DirJumpOnLegacyDialog(string path, HWND dialogHandle)
    {
        // https://github.com/idkidknow/Flow.Launcher.Plugin.DirQuickJump/issues/1
        var controlHandle = PInvoke.FindWindowEx(dialogHandle, HWND.Null, "ComboBoxEx32", null);
        controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "ComboBox", null);
        controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "Edit", null);
        if (controlHandle == HWND.Null)
        {
            _context?.API.LogWarn("DirQuickJump", "Filename edit control not found");
            return;
        }

        Utils.SetWindowText(controlHandle, path);
        var inputSimulator = new WindowsInput.InputSimulator();
        // Alt-O (equivalent to press the Open button) twice. In normal cases it suffices to press once,
        // but when the focus is on an irrelevant folder, that press once will just open the irrelevant one.
        inputSimulator.Keyboard.ModifiedKeyStroke(WindowsInput.VirtualKeyCode.LMENU, WindowsInput.VirtualKeyCode.VK_O);
        inputSimulator.Keyboard.ModifiedKeyStroke(WindowsInput.VirtualKeyCode.LMENU, WindowsInput.VirtualKeyCode.VK_O);
    }

    #region Settings GUI

    public Control CreateSettingPanel()
    {
        var control = new UserControl();
        var grid = new Grid();
        var gridCol1 = new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) };
        var gridCol2 = new ColumnDefinition();
        grid.ColumnDefinitions.Add(gridCol1);
        grid.ColumnDefinitions.Add(gridCol2);
        var text = new TextBlock
        {
            Text = "How to navigate to the path",
            Margin = new Thickness(70, 9, 18, 9),
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(text, 0);
        var comboBox = new ComboBox
        {
            ItemsSource = new[] { "Alt-D", "Ctrl-L" },
            SelectedItem = _settings?.Strategy switch
            {
                Strategy.AltD => "Alt-D",
                Strategy.CtrlL => "Ctrl-L",
                _ => "Alt-D",
            },
            IsEditable = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 9, 18, 9),
        };
        Grid.SetColumn(comboBox, 1);
        comboBox.DropDownClosed += (_, _) =>
        {
            _settings!.Strategy = comboBox.Text switch
            {
                "Alt-D" => Strategy.AltD,
                "Ctrl-L" => Strategy.CtrlL,
                _ => throw new UnreachableException(),
            };
        };
        grid.Children.Add(text);
        grid.Children.Add(comboBox);
        control.Content = grid;
        return control;
    }

    #endregion
}