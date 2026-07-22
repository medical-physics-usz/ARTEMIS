using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;
using USZ_ARTEMIS.Configuration;
using USZ_ARTEMIS.DataQualification;
using USZ_ARTEMIS.UserControls;
using VMS.TPS.Common.Model.API;

// TODO: Replace the following version attributes by creating AssemblyInfo.cs. You can do this in the properties of the Visual Studio project.
[assembly: AssemblyVersion("26.7.22.0")]
[assembly: AssemblyFileVersion("1.0.0.1")]
[assembly: AssemblyInformationalVersion("1.0")]

// Script requires write access.
[assembly: ESAPIScript(IsWriteable = true)]

namespace VMS.TPS
{
    public class Script
    {
        public Script()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Execute(ScriptContext context, System.Windows.Window window/*, ScriptEnvironment environment*/)
        {
            // TODO: set clinicalVersion parameter manually to true for clinical version and to false for TBox. Otherwise Verification plans with a phantom do not work
            bool clinicalVersion = true;

            // TODO: change paths accordingly if they are changed.
            string path = AppPaths.PublishedScriptIconPath;

            try
            {
                DataChecker.CheckContext(context);
            }
            catch (Exception excp)
            {
                System.Windows.MessageBox.Show(excp.Message, "Error message", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Patient patient = context.Patient;
            patient.BeginModifications();

            window.Title = "ARTEMIS v26.7.22.0";
            window.Width = 1200;
            window.Height = 700;

            // Put the USZ icon to the top left corner from the correct folder
            if (File.Exists(path))
            {
                Uri.TryCreate(path, UriKind.RelativeOrAbsolute, out Uri iconUri);
                window.Icon = BitmapFrame.Create(iconUri);
            }

            // Add content to window
            var myWindow = new MainWindow(context, window, clinicalVersion);
            window.Content = myWindow;
        }
    }
}
