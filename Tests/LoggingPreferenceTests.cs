using System;
using System.Reflection;

namespace AudioDataPlugIn
{
    internal static class LoggingPreferenceTests
    {
        private static void Main()
        {
            const string variable = "EACENHANCEMENTS_LOGGING";
            string original = Environment.GetEnvironmentVariable(variable);
            Type runtime = typeof(AudioDataTransfer).Assembly.GetType(
                "AudioDataPlugIn.EnhancementRuntime",
                true);
            MethodInfo initialize = runtime.GetMethod(
                "InitializeLoggingPreference",
                BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo enabled = runtime.GetField(
                "loggingEnabled",
                BindingFlags.NonPublic | BindingFlags.Static);

            try
            {
                Environment.SetEnvironmentVariable(variable, null);
                initialize.Invoke(null, null);
                AssertEqual((bool)enabled.GetValue(null), false, "default preference");

                Environment.SetEnvironmentVariable(variable, "1");
                initialize.Invoke(null, null);
                AssertEqual((bool)enabled.GetValue(null), true, "process override");
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, original);
                initialize.Invoke(null, null);
            }

            Console.WriteLine("Logging preference tests passed.");
        }

        private static void AssertEqual(bool actual, bool expected, string description)
        {
            if (actual != expected)
                throw new Exception("Unexpected " + description + ".");
        }
    }
}
