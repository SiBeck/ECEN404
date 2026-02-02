using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace _403DesktopApp
{
    public partial class MainWindow : Window
    {
        private MainViewModel _viewModel;
        private System.Windows.Point _lastMousePosition;
        private bool _isDragging;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            this.KeyDown += MainWindow_KeyDown;
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            bool shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (e.Key == Key.Left)
            {
                if (shiftPressed)
                {
                    _viewModel.FirstFrameCommand.Execute(null);
                }
                else
                {
                    _viewModel.PreviousFrameCommand.Execute(null);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                if (shiftPressed)
                {
                    _viewModel.LastFrameCommand.Execute(null);
                }
                else
                {
                    _viewModel.NextFrameCommand.Execute(null);
                }
                e.Handled = true;
            }
        }

        private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Delta > 0)
                    _viewModel.ZoomInCommand.Execute(null);
                else
                    _viewModel.ZoomOutCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = sender as Canvas;
            if (canvas != null && _viewModel.CurrentImageSource != null)
            {
                _isDragging = true;
                _lastMousePosition = e.GetPosition(canvas);
                canvas.CaptureMouse();
                canvas.Cursor = Cursors.Hand;
            }
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var canvas = sender as Canvas;
            if (canvas != null && _isDragging)
            {
                _isDragging = false;
                canvas.ReleaseMouseCapture();
                canvas.Cursor = Cursors.Arrow;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvas = sender as Canvas;
                var scrollViewer = FindVisualParent<ScrollViewer>(canvas);
                if (scrollViewer != null)
                {
                    var currentPosition = e.GetPosition(canvas);
                    var offset = currentPosition - _lastMousePosition;
                    scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - offset.X);
                    scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - offset.Y);
                    _lastMousePosition = currentPosition;
                }
            }
        }

        private static T FindVisualParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parentObject = System.Windows.Media.VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindVisualParent<T>(parentObject);
        }
    }
}