using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Data;

namespace OpenCodexLauncherV2
{
    // A message keeps resource references and user values separate. Changing language
    // updates bindings without rebuilding controls or translating user-entered data.
    public sealed class LocalText : INotifyPropertyChanged
    {
        readonly Func<string> render;
        internal LocalText(Func<string> render) { this.render = render; L.Register(this); }
        public string Value { get { return render(); } }
        public override string ToString() { return Value; }
        public event PropertyChangedEventHandler PropertyChanged;
        internal void Refresh() { if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs("Value")); }
        public static implicit operator string(LocalText value) { return value == null ? "" : value.Value; }
        public static LocalText operator +(LocalText a, object b) { return new LocalText(() => a.Value + Convert.ToString(b)); }
        public static LocalText operator +(LocalText a, LocalText b) { return new LocalText(() => a.Value + b.Value); }
        public static LocalText operator +(string a, LocalText b) { return new LocalText(() => a + b.Value); }
    }
    public static class L
    {
        static readonly Dictionary<string, Dictionary<string, string>> resources;
        static readonly List<WeakReference> messages = new List<WeakReference>();
        static readonly object sync = new object();
        public static string Language { get; private set; }
        static L()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("OpenCodexLauncher.localization.json"))
            using (var reader = new StreamReader(stream))
                resources = JsonData.Serializer().Deserialize<Dictionary<string, Dictionary<string, string>>>(reader.ReadToEnd());
            Language = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? "zh" : "en";
        }
        internal static void Register(LocalText text)
        {
            lock (sync) { if (messages.Count > 1000) messages.RemoveAll(x => !x.IsAlive); messages.Add(new WeakReference(text)); }
        }
        public static void SetLanguage(string value)
        {
            Language = value == "zh" ? "zh" : "en";
            LocalText[] alive;
            lock (sync) { messages.RemoveAll(x => !x.IsAlive); alive = messages.Select(x => x.Target as LocalText).Where(x => x != null).ToArray(); }
            foreach (var text in alive) text.Refresh();
        }
        public static string Get(string key) { return resources[key][Language]; }
        public static LocalText M(string key) { return new LocalText(() => Get(key)); }
        public static LocalText Raw(string value) { return new LocalText(() => value); }
        public static LocalText F(string key, params object[] values) { return new LocalText(() => String.Format(Get(key), values)); }
        public static IEnumerable<string> Keys { get { return resources.Keys; } }
        public static bool Complete { get { return resources.Values.All(x => x.ContainsKey("zh") && x.ContainsKey("en") && !String.IsNullOrWhiteSpace(x["en"])); } }
        public static void Bind(DependencyObject target, DependencyProperty property, LocalText value)
        { BindingOperations.SetBinding(target, property, new Binding("Value") { Source = value, Mode = BindingMode.OneWay }); }
    }
}
