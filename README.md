# النجار برنت — RIP لطابعة Epson L1800 🖨♛

نظام RIP احترافي Clean-Room لورشة **النجار برنت** (برج التلي) — واجهة WPF عربية بثيم أسود ملكي + محرك ألوان كامل + طباعة ESC/P2 حقيقية عبر USB.

> السلسلة: Stage 1 محرك الألوان ✅ → Stage 2 الميكانيكا ✅ → Stage 3 الواجهة (62/62) ✅ → Stage 4 التكامل الكامل (47/47) ✅

---

## ⚡ التشغيل السريع (خطوة بخطوة)

### 1) تثبيت .NET 8 SDK — لو مش مثبت

افتح PowerShell عادي (بدون أدمن) ونفّذ:

```powershell
# تنزيل سكربت التثبيت الرسمي من Microsoft
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile "$env:TEMP\dotnet-install.ps1" -UseBasicParsing

# تثبيت على مستوى المستخدم — بدون صلاحيات أدمن
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$env:TEMP\dotnet-install.ps1" -Channel 8.0 -InstallDir "$env:LOCALAPPDATA\Microsoft\dotnet" -NoPath

# تسجيل المسار دائمًا (يخلي أي exe يعرف فين الـ runtime)
[Environment]::SetEnvironmentVariable('DOTNET_ROOT', "$env:LOCALAPPDATA\Microsoft\dotnet", 'User')
[Environment]::SetEnvironmentVariable('Path', "$env:LOCALAPPDATA\Microsoft\dotnet;" + [Environment]::GetEnvironmentVariable('Path','User'), 'User')
```

### 2) التحقق من التثبيت

```powershell
# اقفل PowerShell وافتحه من جديد (عشان الـ Path الجديد) ثم:
dotnet --version          # المفروض: 8.0.427 أو أحدث 8.x
dotnet --list-sdks
```

### 3) بناء المشروع بالكامل

```powershell
cd G:\ai\ElnagarPrint-RIP
dotnet build ElnagarPrint.RIP.sln -c Debug
```

- أول بناء ينزّل NuGet تلقائيًا (Magick.NET + LibUsbDotNet) — محتاج إنترنت.
- النتيجة المطلوبة: `Build succeeded — 0 Error(s)`.
- تحذير `NU1701` عن LibUsbDotNet طبيعي (حزمة .NET Framework شغالة عبر طبقة التوافق).

### 4) تشغيل الواجهة

```powershell
Start-Process "G:\ai\ElnagarPrint-RIP\ElnagarPrint.RIP.UI\bin\Debug\net8.0-windows\ElnagarPrint.RIP.UI.exe"
```

لو ظهرت رسالة «You must install .NET to run this application» → معناها `DOTNET_ROOT` مش متسجل — نفّذ سطر `[Environment]::SetEnvironmentVariable(...)` من خطوة 1 وافتح PowerShell جديد.

> **أو من Visual Studio 2022:** افتح `ElnagarPrint.RIP.sln` → F5.

### 5) اختبار الاتصال بالطابعة 🔌

1. وصّل L1800 وشغّلها.
2. في الواجهة: زر **«🔌 اختبار»** أعلى النافذة — بيجري تشخيص كامل:
   - فحص LibUsb registry (VID=04B8 / PID=1129)
   - فحص طوابير Windows المثبتة
   - فتح USB تجريبي + claim
   - اختبار صلاحية الطابور
3. النتيجة تظهر في السجل الحي مع **توصية عملية**:
   - **USB مباشر (WinUSB)** — الإرسال بايت-ببايت المباشر
   - **Windows Spooler (RAW)** — لو درايفر Epson يحجز الجهاز (الوضع العملي المعتاد للورشة) — الحل التلقائي الافتراضي
4. ثم: **استعراض صورة → معاينة (رؤية التنقيط) → طباعة**.

---

## 🎨 التحكم في الألوان الفاتحة (Lc/Lm) — قفل حقيقي مثبت بالاختبار

تبويب **Ink Ctrl** فيه كارت «التحكم الفرعي في الألوان الفاتحة»:

| التحكم | الأثر الفعلي |
|---|---|
| **قفل 🔒 Light Cyan** | gate=0 → صفر نقاط Lc في خريطة التنقيط → صفر حبر |
| **قفل 🔒 Light Magenta** | gate=0 → صفر نقاط Lm |
| **سلايدر النسبة 0-100%** | تخفيف/تقوية درجات اللون الفاتح |
| **⚡ قفل فوري** | قفل القناتين معًا بضغطة |
| **↩ إرجاع 100%** | فك القفل والرجوع للكامل |

**إثبات تشغيلي** (تجربة فعلية على صور ماجنتا/سماوية صافية):

```
ماجنتا بدون قفل: نقاط Lm =  5396      سماوي بدون قفل: نقاط Lc =  5393
ماجنتا بقفل   : نقاط Lm =     0      سماوي بقفل   : نقاط Lc =     0
✅ PASS — القفل حقيقي: صفر نقاط عند القفل، ونقاط فعلية بدونه
```

المسار: `CheckBox القفل → EffectiveLightCyanGate → PipelineSettings.LightCyanGate → RipPipeline (ink × gate) → Halftone DotMap → ESC/P2 → الطابعة`.

---

## 🗂 بنية المشروع

```
ElnagarPrint.RIP.sln
├── ElnagarPrint.RIP.Core\        ← محرك الـ RIP (net8.0)
│   ├── IccColorEngine.cs         ← الفلاتر الحقيقية: سطوع/تباين/إشباع/LAB/نوايا ICC
│   ├── InkControlProcessor.cs    ← UCR + حدود الحبر + تصحيح القنوات
│   ├── ColorSeparator.cs         ← اشتقاق Lc/Lm + الـ Gates
│   ├── HalftoneProcessor.cs      ← Floyd-Steinberg متعدد المستويات
│   ├── WeaveEngine.cs            ← توزيع Nozzle ضد الـ banding
│   ├── MechanicsControl.cs       ← اتجاه/ارتفاع رأس/قطع/وقفات تجفيف ذكية
│   ├── ESCP2Encoder.cs           ← ترميز VDS 3-passes + أوامر ميكانيكية
│   ├── LayoutComposer.cs         ← تدوير/بلا حواف/N-up/Duplex يدوي
│   ├── RipPipeline.cs            ← منسّق المسار الكامل (نقطة الدخول)
│   ├── UsbPrinterPort.cs         ← LibUsbDotNet مباشر (WinUSB)
│   ├── WindowsSpoolerPort.cs     ← نقل RAW عبر طابور Windows (fallback)
│   └── UsbDiagnostics.cs         ← تشخيص الاتصال 5 خطوات
└── ElnagarPrint.RIP.UI\          ← واجهة WPF (net8.0-windows)
    ├── MainWindow.xaml           ← 7 تبويبات + معاينة + سجل + شريط تقدم
    ├── Views\Tab*.xaml           ← Main/Layout/Color/Correction/Consumption/InkCtrl/Options
    ├── ViewModels\MainViewModel  ← نقطة الربط الوحيدة BuildPipelineSettings()
    └── App.xaml                  ← الثيم الأسود الملكي (ذهبي #FFD700/#D4AF37)
```

## 📊 سير البيانات

```
الواجهة (36 خاصية) → PipelineSettings
→ Magick.NET تحميل (8/16-bit حقيقي، EXIF، ≤15MP)
→ IccColorEngine: نية ICC (Inksave ⚡) → فلاتر → LAB → وضع لون → CMYK
→ InkControlProcessor: UCR → حدود → تصحيح → Lc/Lm → Gates (أقفال)
→ LayoutComposer → Halftone → Weave → ESCP2Encoder (VDS + ميكانيكا)
→ USB مباشر ← فشل → Windows Spooler RAW  ×  نسخ × أوجه
```

## 📄 التقارير

- [`docs/Stage3_Audit_Report.md`](docs/Stage3_Audit_Report.md) — تدقيق الواجهة (62/62)
- [`docs/Stage4_Audit_Report.md`](docs/Stage4_Audit_Report.md) — تدقيق التكامل النهائي (47/47)

---

**الورشة:** النجار برنت — برج التلي | **الطابعة المستهدفة:** Epson L1800 (VID=0x04B8, PID=0x1129، 180 nozzle، 6 أحبار C/M/Y/K/Lc/Lm)
