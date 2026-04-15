using System;
using System.Windows;
using ICSharpCode.AvalonEdit.Document;

namespace MossadStudio
{
    public partial class FindReplaceWindow : Window
    {
        private MainWindow _parent;

        public FindReplaceWindow(MainWindow parent)
        {
            InitializeComponent();
            _parent = parent;
            
            // Auto-populate find selection
            var editor = _parent.GetActiveEditor();
            if (editor != null && editor.SelectionLength > 0 && !editor.SelectedText.Contains("\n"))
            {
                txtFind.Text = editor.SelectedText;
            }
            
            txtFind.Focus();
        }

        private void FindNext_Click(object sender, RoutedEventArgs e)
        {
            var editor = _parent.GetActiveEditor();
            if (editor == null) return;

            string query = txtFind.Text;
            if (string.IsNullOrEmpty(query)) return;

            StringComparison comparison = chkMatchCase.IsChecked == true 
                ? StringComparison.CurrentCulture 
                : StringComparison.CurrentCultureIgnoreCase;

            int startIndex = editor.SelectionStart + editor.SelectionLength;
            int index = editor.Text.IndexOf(query, startIndex, comparison);
            
            // Loop array wrapper
            if (index < 0) 
            {
                index = editor.Text.IndexOf(query, 0, comparison);
            }

            if (index >= 0)
            {
                editor.Select(index, query.Length);
                var loc = editor.Document.GetLocation(index);
                editor.ScrollTo(loc.Line, loc.Column);
                editor.Focus();
            }
            else
            {
                MessageBox.Show($"Cannot find \"{query}\"", "Mossad Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Replace_Click(object sender, RoutedEventArgs e)
        {
            var editor = _parent.GetActiveEditor();
            if (editor == null || editor.IsReadOnly) return;

            string query = txtFind.Text;
            if (string.IsNullOrEmpty(query)) return;

            StringComparison comparison = chkMatchCase.IsChecked == true 
                ? StringComparison.CurrentCulture 
                : StringComparison.CurrentCultureIgnoreCase;

            if (editor.SelectionLength > 0 && editor.SelectedText.Equals(query, comparison))
            {
                editor.Document.Replace(editor.SelectionStart, editor.SelectionLength, txtReplace.Text);
            }
            
            FindNext_Click(sender, e);
        }

        private void ReplaceAll_Click(object sender, RoutedEventArgs e)
        {
            var editor = _parent.GetActiveEditor();
            if (editor == null || editor.IsReadOnly) return;

            string query = txtFind.Text;
            if (string.IsNullOrEmpty(query)) return;

            StringComparison comparison = chkMatchCase.IsChecked == true 
                ? StringComparison.CurrentCulture 
                : StringComparison.CurrentCultureIgnoreCase;

            int count = 0;
            int offset = 0;
            
            // We loop and replace, ensuring we don't infinitely lock up if offset logic breaks
            editor.BeginChange();
            try 
            {
                while (true)
                {
                    int index = editor.Text.IndexOf(query, offset, comparison);
                    if (index < 0) break;
                    
                    editor.Document.Replace(index, query.Length, txtReplace.Text);
                    offset = index + txtReplace.Text.Length;
                    count++;
                }
            } 
            finally 
            {
                editor.EndChange();
            }

            MessageBox.Show($"{count} occurrence(s) replaced.", "Mossad Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
