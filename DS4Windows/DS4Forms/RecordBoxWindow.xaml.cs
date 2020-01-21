using System;
using System.Windows;

namespace DS4WinWPF.DS4Forms
{
    /// <summary>
    /// Interaction logic for RecordBoxWindow.xaml
    /// </summary>
    public partial class RecordBoxWindow : Window
    {
        public event EventHandler Saved;

        public RecordBoxWindow(int deviceNum, DS4Windows.DS4ControlSettings settings)
        {
            InitializeComponent();

            RecordBox box = new RecordBox(deviceNum, settings, false);
            mainPanel.Children.Add(box);

            box.Save += RecordBox_Save;
            box.Cancel += Box_Cancel;
        }

        private void Box_Cancel(object sender, EventArgs e)
        {
            Close();
        }

        private void RecordBox_Save(object sender, EventArgs e)
        {
            Saved?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}
