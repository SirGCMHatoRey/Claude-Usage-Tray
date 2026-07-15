using System.Windows;
using System.Windows.Input;

namespace ClaudeUsageTray.Presentation.Widget;

public partial class FloatingWidget : Window
{
    public FloatingWidget(FloatingWidgetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PositionBottomRight();
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void PositionBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Bottom - Height - 16;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is IDisposable d) d.Dispose();
        base.OnClosed(e);
    }
}
