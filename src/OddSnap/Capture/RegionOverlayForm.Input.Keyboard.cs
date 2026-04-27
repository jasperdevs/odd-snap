using System.Drawing;
using System.Windows.Forms;
using OddSnap.Models;

namespace OddSnap.Capture;

public sealed partial class RegionOverlayForm
{
    // ProcessCmdKey always receives ESC (OnKeyDown sometimes doesn't)
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape)
        {
            Cancel();
            return true;
        }
        if (_flyoutOpen && TryHandleAnnotationToolHotkey(keyData & Keys.KeyCode))
        {
            CloseMoreToolsDropdown();
            RefreshToolbar();
            Invalidate();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            Cancel();
            return;
        }

        // Undo must work in all states (emoji placing, typing, etc.)
        if (e.KeyCode == Keys.Z && e.Control && _undoStack.Count > 0)
        {
            var last = RemoveLastAnnotation();
            _redoStack.Add(last);
            // Update step counter when undoing a step number
            if (last is StepNumberAnnotation)
            {
                var remaining = _undoStack.OfType<StepNumberAnnotation>().LastOrDefault();
                _nextStepNumber = remaining != null ? remaining.Number + 1 : 1;
            }
            Invalidate(InflateForRepaint(GetAnnotationBounds(last)));
            return;
        }

        if ((e.KeyCode == Keys.Y && e.Control || e.KeyCode == Keys.Z && e.Control && e.Shift) && _redoStack.Count > 0)
        {
            var annotation = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            RestoreAnnotation(annotation);
            if (annotation is StepNumberAnnotation step)
                _nextStepNumber = Math.Max(_nextStepNumber, step.Number + 1);
            Invalidate(InflateForRepaint(GetAnnotationBounds(annotation)));
            return;
        }

        // All search/text input is handled by off-screen TextBoxes
        if (_emojiPickerOpen) return;

        // Emoji placing: Tab re-opens picker
        if (_mode == CaptureMode.Emoji && _isPlacingEmoji)
        {
            if (e.KeyCode == Keys.Tab) { _emojiPickerOpen = true; _isPlacingEmoji = false; ShowEmojiSearchBox(); RefreshToolbar(); }
            return;
        }

        if (_fontPickerOpen) return;
        if (_isTyping) return;
        if (TryHandleAnnotationToolHotkey(e.KeyCode))
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            RefreshToolbar();
            Invalidate();
            return;
        }

        // Delete selected annotation
        if (e.KeyCode == Keys.Delete && _mode == CaptureMode.Select && _selectedAnnotationIndex >= 0 && _selectedAnnotationIndex < _undoStack.Count)
        {
            var bounds = InflateForRepaint(GetAnnotationBounds(_undoStack[_selectedAnnotationIndex]));
            _undoStack.RemoveAt(_selectedAnnotationIndex);
            _redoStack.Clear();
            MarkCommittedAnnotationsDirty();
            _selectedAnnotationIndex = -1;
            Invalidate(bounds);
            return;
        }
    }

    private bool TryHandleAnnotationToolHotkey(Keys keyCode)
    {
        var settings = Services.SettingsService.LoadStatic();
        if (settings is null)
            return false;

        uint mod = 0;
        if ((ModifierKeys & Keys.Control) != 0) mod |= Native.User32.MOD_CONTROL;
        if ((ModifierKeys & Keys.Alt) != 0) mod |= Native.User32.MOD_ALT;
        if ((ModifierKeys & Keys.Shift) != 0) mod |= Native.User32.MOD_SHIFT;
        uint vk = unchecked((uint)(keyCode & Keys.KeyCode));

        var toolId = settings.FindAnnotationToolId(mod, vk, _visibleTools.Where(t => t.Group == 1).Select(t => t.Id));
        if (toolId is null)
            return false;

        var tool = _visibleTools.FirstOrDefault(t => string.Equals(t.Id, toolId, StringComparison.OrdinalIgnoreCase));
        if (tool?.Mode is not { })
            return false;

        SetTool(tool);
        return true;
    }
}
