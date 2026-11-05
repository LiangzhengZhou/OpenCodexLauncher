using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace OpenCodexLauncherV2
{
    public sealed partial class MainWindow
    {
        UIElement AggregationPage()
        {
            var panel = new StackPanel();
            panel.Children.Add(SectionHeader(L.M("aggregation.title"), L.M("aggregation.note")));
            var status = Text("");
            panel.Children.Add(Card(status));
            var actions = new WrapPanel();
            actions.Children.Add(AsyncBtn(L.M("aggregation.enable"), async () => {
                if (!await Healthy()) await StartProxy();
                if (NativeActivation.Current.Enabled) NativeActivation.Current.Disable();
                AggregationMode.Enable(paths);
                SetText(status, L.M("aggregation.enabled") + Environment.NewLine + L.M("aggregation.restart"));
                Log(L.M("aggregation.started"));
            }));
            actions.Children.Add(AsyncBtn(L.M("aggregation.disable"), () => {
                AggregationMode.Disable(paths);
                SetText(status, L.M("aggregation.disabled") + Environment.NewLine + L.M("aggregation.restart"));
                Log(L.M("aggregation.stopped"));
                return Task.FromResult(0);
            }));
            panel.Children.Add(actions);
            panel.Children.Add(Text(L.M("aggregation.scope")));
            panel.Loaded += delegate {
                SetText(status, AggregationMode.Enabled
                    ? L.M("aggregation.enabled") + " · " + AggregationMode.Port + " · " + AggregationMode.RouteCount
                    : L.M("aggregation.disabled"));
            };
            return panel;
        }
    }
}
