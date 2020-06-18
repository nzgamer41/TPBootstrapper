using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace TPBootstrapper
{
    public class LogHelper
    {
        private bool _isEnabled;
        private StreamWriter sw;
        public LogHelper(bool enable = false)
        {
            _isEnabled = enable;
            try
            {
                sw = new StreamWriter("TPInstaller.log");
                sw.AutoFlush = true;
                if (Assembly.GetExecutingAssembly().GetName().Version.ToString() == "1.0.0.0")
                {
                    sw.WriteLine("TeknoParrot Installer Debug Build");
                }
                else
                {
                    sw.WriteLine("TeknoParrot Installer Version: " +
                                 Assembly.GetExecutingAssembly().GetName().Version.ToString() +
                                 " " + DateTime.Today);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.StackTrace);
                Debug.WriteLine("Error, couldn't access file! Reverting to normal logging..");
                _isEnabled = false;
            }
        }

        /// <summary>
        /// Writes out the specified string to the console and to a Text file if enabled.
        /// </summary>
        /// <param name="writeOut"></param>
        public void WriteLine(string writeOut)
        {
            Debug.WriteLine(writeOut);
            if (_isEnabled)
            {
                sw.WriteLine(writeOut);
                
            }
        }

    }
}
