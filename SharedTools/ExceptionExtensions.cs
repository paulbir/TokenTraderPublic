using System;
using System.Collections;

namespace SharedTools
{
    public static class ExceptionExtensions
    {
        public static string GetExceptionData(this Exception ex, string nesting)
        {
            string exceptionStr = nesting + " exception \n\nData:\n";

            foreach (DictionaryEntry entry in ex.Data) exceptionStr += "KEY = " + entry.Key + "; VALUE = " + entry.Value + "\n";
            exceptionStr += "\nMessage: " + ex.Message + "\n\nTargetSite: " + ex.TargetSite + "\n\nStackTrace:\n" + ex.StackTrace + 
                      "\n\n----------------------------------------------------------------------------------------------------------------------------\n\n";

            return exceptionStr;
        }

        public static string MakeString(this Exception ex)
        {
            string exceptionStr = "\n" + ex.GetExceptionData("OUTER");

            if (ex.InnerException != null)
            {
                Exception localException = ex.InnerException;
                while (localException != null)
                {
                    exceptionStr += localException.GetExceptionData("INNER");
                    localException = localException.InnerException;
                }
            }

            return exceptionStr;
        }
    }
}
