#pragma warning disable 1591
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Flow.Launcher.Plugin.DirQuickJump
{
    public class DirQuickJump : IPlugin, IContextMenu
    {
        private PluginInitContext? _context;

        public void Init(PluginInitContext context)
        {
            _context = context;
        }

        public List<Result> Query(Query query)
        {
            if (_context is null) throw new UnreachableException(); // Guaranteed by the caller

            Type shellApplicationType = Type.GetTypeFromProgID("Shell.Application", true)!;
            dynamic shellApplication = Activator.CreateInstance(shellApplicationType)!;
            List<Result> urls = [];
            foreach (var window in shellApplication.Windows())
            {
                string name = window.Document.Folder.Self.Name;
                string path = window.Document.Folder.Self.Path;
                if (
                    query.Search.Trim() != "" &&
                    !_context.API.FuzzySearch(query.Search, path).Success
                ) { continue; }
                var result = new Result
                {
                    Title = name,
                    SubTitle = path,
                    IcoPath = "icon.png",
                    Action = ctx =>
                    {
                        if (ctx.SpecialKeyState.CtrlPressed) return CopyAction(path);
                        return JumpAction(path);
                    },
                    ContextData = (name, path),
                };
                urls.Add(result);
            }
            return urls;
        }

        public List<Result> LoadContextMenus(Result selectedResult)
        {
            var (name, path) = ((string, string))selectedResult.ContextData;
            var jumpAction = new Result
            {
                Title = "Jump",
                SubTitle = path,
                IcoPath = "icon.png",
                Action = ctx => JumpAction(path),
            };
            var showAction = new Result
            {
                Title = "Show the path",
                IcoPath = "icon.png",
                Action = ctx => ShowAction(path),
            };
            var copyAction = new Result
            {
                Title = "Copy the path",
                IcoPath = "icon.png",
                Action = ctx => CopyAction(path),
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
            var flowHandle = PInvoke.GetForegroundWindow();
            _context?.API.LogInfo("DirQuickJump", $"Jumping to {path}");
            var t = new Thread(() =>
            {
                // Jump after flow launcher window vanished (after JumpAction returned true)
                // and the dialog had been in the foreground. The class name of a dialog window is "#32770".
                bool timeOut = !SpinWait.SpinUntil(() => GetForegroundWindowClassName() == "#32770", 1000);
                if (timeOut) {
                    _context?.API.LogWarn("DirQuickJump", "Dialog window not found");
                    return;
                };
                // Assume that the dialog is in the foreground now
                JumpOnCurrentDialog(path);
            });
            t.Start();
            return true;

            unsafe static string? GetForegroundWindowClassName()
            {
                var handle = PInvoke.GetForegroundWindow();
                return Utils.GetClassName(handle);
            }
        }

        private unsafe void JumpOnCurrentDialog(string path)
        {
            // Alt-D to focus on the path input box
            var inputSimulator = new WindowsInput.InputSimulator();
            inputSimulator.Keyboard.ModifiedKeyStroke(WindowsInput.VirtualKeyCode.MENU, WindowsInput.VirtualKeyCode.VK_D);

            // Get the handle of the path input box and then set the text.
            // The window with class name "ComboBoxEx32" is not visible when the path input box is not with the keyboard focus.
            var dialogHandle = PInvoke.GetForegroundWindow();
            var controlHandle = PInvoke.FindWindowEx(dialogHandle, HWND.Null, "WorkerW", null);
            controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "ReBarWindow32", null);
            controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "Address Band Root", null);
            controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "msctls_progress32", null);
            controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "ComboBoxEx32", null);
            bool timeOut = !SpinWait.SpinUntil(() =>
            {
                var style = PInvoke.GetWindowLong(controlHandle, WINDOW_LONG_PTR_INDEX.GWL_STYLE);
                return (style & (int)WINDOW_STYLE.WS_VISIBLE) != 0;
            }, 1000);
            if (timeOut)
            {
                _context?.API.LogWarn("DirQuickJump", $"Alt-D failed. ComboBoxEx32 handle: {controlHandle}");
                return;
            }
            controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "ComboBox", null);
            controlHandle = PInvoke.FindWindowEx(controlHandle, HWND.Null, "Edit", null);

            Utils.SetWindowText(controlHandle, path);
            inputSimulator.Keyboard.KeyPress(WindowsInput.VirtualKeyCode.RETURN);
        }

        internal static class Utils
        {
            internal static unsafe string? GetClassName(HWND handle)
            {
                fixed (char* buf = new char[256])
                {
                    if (PInvoke.GetClassName(handle, buf, 256) == 0) return null;
                    return new string(buf);
                }
            }

            internal static unsafe nint SetWindowText(HWND handle, string text)
            {
                fixed (char* textPtr = text)
                {
                    var result = PInvoke.SendMessage(handle, PInvoke.WM_SETTEXT, 0, (nint)textPtr);
                    return result.Value;
                }
            }
        }
    }
}
