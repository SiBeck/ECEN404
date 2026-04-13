using System.Windows;

namespace _403DesktopApp
{
    public partial class ForgotPasswordWindow : Window
    {
        public ForgotPasswordWindow()
        {
            InitializeComponent();
        }

        private void Verify_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ForgotPasswordViewModel vm)
            {
                bool ok = vm.Verify();
                if (ok)
                {
                    MessageBox.Show(vm.StatusMessage, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    DialogResult = true;
                }
                else
                {
                    MessageBox.Show(vm.StatusMessage, "Verification failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}