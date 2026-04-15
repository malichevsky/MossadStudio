using System.Windows;
using System.Windows.Input;

namespace MossadStudio
{
    public partial class PromptWindow : Window
    {
        public string ResponseText { get; private set; } = string.Empty;

        public PromptWindow(string title, string message, string defaultResponse = "")
        {
            InitializeComponent();
            TxtTitle.Text = title;
            TxtMessage.Text = message;
            TxtInput.Text = defaultResponse;
            TxtInput.SelectAll();
            TxtInput.Focus();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            this.Close();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            ResponseText = TxtInput.Text;
            DialogResult = true;
            this.Close();
        }

        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Ok_Click(sender, null);
            }
        }
    }
}
