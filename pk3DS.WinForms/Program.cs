using System;
using System.Windows.Forms;

#if !DEBUG
using System.Threading;
#endif

namespace pk3DS.WinForms;

internal static class Program
{
    /// <summary>
    /// The main entry point for the application.
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
#if !DEBUG
            // Add the event handler for handling UI thread exceptions to the event.
            Application.ThreadException += UIThreadException;

            // Set the unhandled exception mode to force all Windows Forms errors to go through our handler.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // Add the event handler for handling non-UI thread exceptions to the event. 
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
#endif

        // Headless native bridge diagnostic: use exactly the loader used by Batch Gen6.
        if(args.Length==4&&args[0]=="--native-gen6-replay")
        {
            try
            {
                BatchGen6ActionExecutor.ExecuteNative(args[1],System.IO.File.ReadAllText(args[2]),int.Parse(args[3],System.Globalization.CultureInfo.InvariantCulture));
            }
            catch(Exception ex){Console.Error.WriteLine(ex);Environment.ExitCode=1;}
            return;
        }
        // Run the application
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        ModernUI.EnableGlobalStyling();
        if(args.Length==2&&args[0]=="--relearner-preview")
        {
            var form=new Main();
            form.Shown+=(_,_) =>
            {
                form.Hide();
                try
                {
                    form.RunRelearnerPreviewBridge(args[1]);
                    Environment.ExitCode=0;
                }
                catch(Exception ex)
                {
                    try
                    {
                        using var node=System.Text.Json.JsonDocument.Parse(
                            System.IO.File.ReadAllText(args[1]));
                        string stage=node.RootElement.GetProperty("StageRoot").GetString();
                        if(!string.IsNullOrWhiteSpace(stage))
                        {
                            System.IO.Directory.CreateDirectory(stage);
                            System.IO.File.WriteAllText(
                                System.IO.Path.Combine(stage,"relearner-preview-error.txt"),ex.ToString());
                        }
                    }
                    catch { }
                    Environment.ExitCode=1;
                }
                finally{Application.Exit();}
            };
            Application.Run(form);
        }
        else if(args.Length==2&&args[0]=="--batch-bridge")
        {
            var form=new Main();
            form.Shown+=async (_,_) =>
            {
                form.Hide();
                try
                {
                    await form.RunBatchBridge(args[1]);
                    Environment.ExitCode=0;
                }
                catch(Exception ex)
                {
                    try
                    {
                        string request=System.IO.File.ReadAllText(args[1]);
                        using var node=System.Text.Json.JsonDocument.Parse(request);
                        string folder=node.RootElement.GetProperty("OutputDirectory").GetString();
                        if(!string.IsNullOrWhiteSpace(folder))
                        {
                            System.IO.Directory.CreateDirectory(folder);
                            System.IO.File.WriteAllText(
                                System.IO.Path.Combine(folder,"3ds-progressive-batch-error.txt"),
                                ex.ToString());
                        }
                    }
                    catch { }
                    Environment.ExitCode=1;
                }
                finally{Application.Exit();}
            };
            Application.Run(form);
        }
        else Application.Run(new Main());
    }

#if !DEBUG
        // Handle the UI exceptions by showing a dialog box, and asking the user whether or not they wish to abort execution.
        private static void UIThreadException(object sender, ThreadExceptionEventArgs t)
        {
            DialogResult result = DialogResult.Cancel;
            try
            {
                // Todo: make this translatable
                ErrorWindow.ShowErrorDialog("An unhandled exception has occurred." + Environment.NewLine + "You can continue running the program (albeit with potential side-effects), but please report this error.", t.Exception, true);
            }
            catch
            {
                try
                {
                    // Todo: make this translatable
                    MessageBox.Show("A fatal error has occurred in pk3DS, and the details could not be displayed.  Please report this to the author.", "pk3DS Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                }
                finally
                {
                    Application.Exit();
                }
            }

            // Exits the program when the user clicks Abort.
            if (result == DialogResult.Abort)
                Application.Exit();
        }

        // Handle the UI exceptions by showing a dialog box, and asking the user whether
        // or not they wish to abort execution.
        // NOTE: This exception cannot be kept from terminating the application - it can only 
        // log the event, and inform the user about it. 
        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var ex = (Exception)e.ExceptionObject;
                // Todo: make this translatable
                ErrorWindow.ShowErrorDialog("An unhandled exception has occurred." + Environment.NewLine + "The program must now close.", ex, false);
            }
            catch
            {
                try
                {
                    // Todo: make this translatable
                    MessageBox.Show("A fatal non-UI error has occurred in PKHeX, and the details could not be displayed.  Please report this to the author.", "PKHeX Error", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                }
                finally
                {
                    Application.Exit();
                }
            }
        }
#endif
}