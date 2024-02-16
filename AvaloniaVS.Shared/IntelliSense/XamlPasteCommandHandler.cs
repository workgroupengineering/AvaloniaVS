using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Ide.CompletionEngine;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using IServiceProvider = System.IServiceProvider;

namespace AvaloniaVS.IntelliSense
{
    /// <summary>
    /// Handles key presses for the Avalonia XAML intellisense completion.
    /// </summary>
    /// <remarks>
    /// Adds a command handler to text views and listens for keypresses which should cause a
    /// completion to be opened or comitted.
    /// 
    /// Yes, this is horrible, but it's apparently the official way to do this. Eurgh.
    /// </remarks>
    internal class XamlPasteCommandHandler : IOleCommandTarget
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ICompletionBroker _completionBroker;
        private readonly IOleCommandTarget _nextCommandHandler;
        private readonly ITextView _textView;
        private readonly CompletionEngine _engine;
        private ICompletionSession _session;

        public XamlPasteCommandHandler(
            IServiceProvider serviceProvider,
            ICompletionBroker completionBroker,
            ITextView textView,
            IVsTextView textViewAdapter,
            CompletionEngine completionEngine)
        {
            _serviceProvider = serviceProvider;
            _completionBroker = completionBroker;
            _textView = textView;
            _engine = completionEngine;

            // Add ourselves as a command to the text view.
            textViewAdapter.AddCommandFilter(this, out _nextCommandHandler);
        }

        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return _nextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // If we're in an automation function, move to the next command.
            if (VsShellUtilities.IsInAutomationFunction(_serviceProvider))
            {
                return _nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
            }

            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97
                && (VSConstants.VSStd97CmdID)nCmdID == VSConstants.VSStd97CmdID.Paste)
            {
                List<(string Namespace, int Start, int End)> clrNamespaces = new();
                if (System.Windows.Clipboard.GetDataObject() is { } @do && @do.GetFormats().Contains(Constants.DataFormats.Avalonia_DevTools_Selector))
                {
                    var data = @do.GetData(Constants.DataFormats.Avalonia_DevTools_Selector);
                    if (data != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Clip Type: {data.GetType()}");
                        if (data is System.IO.MemoryStream ms)
                        {
                            var buffer = ms.GetBuffer();
                            var source = Encoding.Unicode.GetString(buffer, 0, buffer.Length - 2);
                            System.Diagnostics.Debug.WriteLine(source);
                            //MemoryMarshal.
                        }
                        return VSConstants.S_OK;
                    }
                }
            }

            return _nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            static void Extract_ClrNamespace(string segment, ref List<(string Namespace, int Start, int End)> namespaces)
            {
                var startIndex = segment.IndexOf('{') + 1;
                if (startIndex > 0)
                {
                    var endIndex = segment.LastIndexOf('}') - 1;
                    if (endIndex > startIndex)
                    {
                        namespaces.Add((segment.Substring(startIndex, endIndex), startIndex, endIndex));
                    }

                }
            }
        }
    }
}
