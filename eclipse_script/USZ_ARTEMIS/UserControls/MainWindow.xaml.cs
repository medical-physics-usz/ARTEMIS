using System.Windows;
using System.Windows.Controls;
using VMS.TPS.Common.Model.API;

namespace USZ_ARTEMIS.UserControls
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    /// 
    public partial class MainWindow : UserControl
    {
        private ScriptContext context;
        private System.Windows.Window window;
        private StartPage startPage;
        private string aboutMsg = "Aria plugin-in program for the preparation of patient data before optimization. \n\n  " +
            "Automatically performed steps: \n " +
            "- PTV ring structure creation \n " +
            "- SBRT ring structure creation (if requested) \n " +
            "- SIB ring structure creation (if requested) \n " +
            "- Couch structures creation (if requested) \n " +
            "- Air in PTV check & structure creation (if requested) \n " +
            "- High density areas in body check & structure creation if needed \n " +
            "- Removal of high density markers from the body \n " +
            "- Objective creation \n\n " +
            "cc Riikka Ruuth 2021";

        public MainWindow(ScriptContext context, System.Windows.Window window, bool clinicalVersion = true)
        {
            InitializeComponent();
            this.context = context;
            this.window = window;
            this.startPage = new StartPage(context, clinicalVersion);
            myStack.Children.Add(startPage);
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(aboutMsg);
        }

        public void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            
        }
    }
}
