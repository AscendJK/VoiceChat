namespace VoiceChat.App.Properties {
    internal sealed partial class Settings : System.Configuration.ApplicationSettingsBase {
        private static Settings defaultInstance = ((Settings)(System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        public static Settings Default => defaultInstance;
        public string LastCaptureDevice {
            get { return ((string)(this["LastCaptureDevice"])); }
            set { this["LastCaptureDevice"] = value; }
        }
    }
}
