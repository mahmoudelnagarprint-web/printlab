using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ElnagarPrint.RIP.Core;

namespace ElnagarPrint.RIP.UI.ViewModels;

/// <summary>
/// أمر الطباعة غير المتزامن — يشغّل الـ pipeline في خيط خلفي كامل
/// حتى لا تتجمد الواجهة أثناء المعالجة أو إرسال البيانات للطابعة
/// </summary>
public class AsyncPrintCommand : ICommand
{
    private readonly MainViewModel _vm;
    private CancellationTokenSource? _cts;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;
    public event EventHandler<string>? StageChanged;

    public AsyncPrintCommand(MainViewModel vm) => _vm = vm;

    public bool CanExecute(object? parameter) => !_isExecuting && _vm.CanPrint;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(null)) return;

        _isExecuting = true;
        _cts = new CancellationTokenSource();
        NotifyCanExecuteChanged();

        var imagePath = _vm.CurrentImagePath!;
        var settings  = _vm.BuildPipelineSettings();

        try
        {
            _vm.IsBusy = true;
            _vm.ProgressPercent = 0;
            _vm.Log($"🖨 بدء طباعة: {_vm.CurrentImageName}");

            var result = await Task.Run(() =>
            {
                // 🚀 نقطة الدخول الفعلية للمحرك — خيط خلفي كامل
                var pipeline = new RipPipeline(settings);

                // تقدم حقيقي من داخل الـ pipeline (مراحل ①-⑨) — مرشح للتقليل من الضوضاء
                int lastPct = -1;
                pipeline.Progress += (pct, msg) =>
                {
                    if (pct == lastPct) return;
                    lastPct = pct;
                    Report(pct, msg);
                };

                return pipeline.PrintImage(imagePath, autoOpenPrinter: true);
            }, _cts.Token);

            if (result.Success)
            {
                _vm.Log($"✅ ناجح — {result.ImageWidth}×{result.ImageHeight}px | {result.BytesSent:N0} بايت | {result.TotalTime.TotalSeconds:F1}s");
                _vm.Log($"   فصل ألوان: {result.ColorSepTime.TotalMilliseconds:F0}ms | تنقيط: {result.HalftoneTime.TotalMilliseconds:F0}ms | ترميز: {result.EncodeTime.TotalMilliseconds:F0}ms | إرسال: {result.UsbTime.TotalSeconds:F1}s");
                _vm.Log($"   النقل: {result.TransportUsed} | نسخ: {result.CopiesSent} | أوجه: {result.SidesPrinted}");
                foreach (var step in result.LayoutSteps)
                    _vm.Log($"   📐 {step}");
                if (result.InkEstimate is { } est)
                    _vm.Log($"   💧 استهلاك فعلي: {est.TotalMilliliters:F2} مل — {est.TotalCostEGP:F2} ج.م");
            }
            else
            {
                _vm.Log($"❌ فشل: {result.Error}");
            }
        }
        catch (OperationCanceledException)
        {
            _vm.Log("🛑 أُلغيت عملية الطباعة بواسطة المستخدم.");
        }
        catch (Exception ex)
        {
            _vm.Log($"❌ خطأ غير متوقع: {ex.Message}");
        }
        finally
        {
            _isExecuting = false;
            _vm.IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            NotifyCanExecuteChanged();
        }
    }

    /// <summary>إلغاء العملية الجارية</summary>
    public void Cancel() => _cts?.Cancel();

    private void Report(int pct, string text)
    {
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _vm.ProgressPercent = pct;
            _vm.ProgressText = text;
            _vm.Log(text);
        });
        StageChanged?.Invoke(this, text);
    }

    public void NotifyCanExecuteChanged()
        => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            CanExecuteChanged?.Invoke(this, EventArgs.Empty));
}