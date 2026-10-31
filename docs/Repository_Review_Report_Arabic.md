# تقرير مراجعة ريبو PrintLab / ElnagarPrint RIP

**الريبو:** `mahmoudelnagarprint-web/printlab`  
**الفرع والنسخة المفحوصة:** `main` — commit `2755f85`  
**تاريخ المراجعة:** 2026-10-31  
**نطاق المراجعة:** البناء، المسار اللوني، فصل القنوات، التنقيط، التشفير ESC/P2، النقل USB/Spooler، واجهة WPF، وميزات Light Cyan / Light Magenta.

## الحكم النهائي

البرنامج ليس وهميًا بالكامل. يوجد Core فعلي يمكن بناؤه وتشغيله، وواجهة WPF، ومسار معالجة صورة حتى خرائط التنقيط، وقفل Lc/Lm مربوط فعليًا بالإعدادات ويؤثر على الناتج. لكن وصف الريبو بأنه «جاهز للطباعة الفعلية» أو «تكامل كامل 100%» غير مدعوم حاليًا؛ لأن طبقة `ESCP2Encoder` لا تطابق بنية أوامر Epson ESC/P2 الموثقة، كما أن اختبار الطابعة الحقيقي لم يُنفّذ داخل هذه المراجعة.

**التقييم الحالي:**

- البناء البرمجي: **يعمل**، مع 18 تحذيرًا.
- المعاينة وفصل الألوان: **تعمل مبدئيًا** على صور اختبارية.
- قفل Light Cyan وLight Magenta: **يعمل فعليًا داخل الـ Core**.
- إدارة الألوان ICC: **جزئية وليست ICC حقيقية كاملة**.
- USB/Spooler: **كود اتصال موجود، لكن غير مثبت على جهاز Windows مع Epson L1800**.
- الإرسال إلى Epson L1800: **غير صالح للاعتماد الإنتاجي قبل إصلاح Encoder والتحقق بطابعة فعلية**.
- عدد من ميزات الواجهة: **موجودة شكليًا أو محفوظة، لكن لا يستهلكها المحرك كما توحي الواجهة**.

## 1. ما تم تشغيله والتحقق منه

تم تثبيت .NET SDK 8.0.427 مؤقتًا خارج الريبو، ثم تنفيذ البناء بالأمر التالي لأن المشروع WPF ويُبنى من Linux:

```bash
dotnet build ElnagarPrint.RIP.sln -c Debug -p:EnableWindowsTargeting=true
```

النتيجة:

```text
Build succeeded
0 Error(s)
18 Warning(s)
```

أهم تحذير هو `NU1701`: الحزمة `LibUsbDotNet 2.2.8` استُعيدت من حزم .NET Framework القديمة لمشروع `net8.0`. نجاح البناء لا يثبت توافق التشغيل على Windows أو صحة Bulk Transfer.

تم كذلك إنشاء اختبار خارجي مؤقت لا يغيّر الريبو. النتائج على صور صناعية صافية كانت:

```text
magenta-open:       Lm=1530
magenta-Lm-locked:  Lm=0
cyan-open:          Lc=1531
cyan-Lc-locked:     Lc=0
```

هذا يثبت أن الـ gate يصل إلى `PreviewHalftone` ثم إلى `HalftoneResult`، وليس مجرد نص في الواجهة. الاختبار يثبت منطق القفل داخل البرنامج فقط، ولا يثبت أن الطابعة ستستقبل البيانات الصحيحة.

تم أيضًا اختبار صورة RGB وصورة grayscale، ومرّتا داخل المعاينة دون استثناء في حالة الاختبار الحالية.

## 2. الأخطاء الحرجة

### F-01 — Encoder لا يستخدم بنية Epson ESC/P2 الصحيحة

**الخطورة: حرجة — تمنع اعتماد الطباعة.**  
**الموضع:** `ElnagarPrint.RIP.Core/ESCP2Encoder.cs:33-47, 293-335, 437-444`.

الكود يعرّف ويستخدم `GS v 0` و`GS v 1`، ثم يرسل `ESC *` لكل قناة. هذه صياغة أقرب إلى أوامر raster الخاصة بطابعات ESC/POS، وليست مسار Epson ESC/P2 inkjet الموثق في مرجع Epson. المرجع الرسمي يعرّف رسومات ESC/P2 عبر `ESC .`، ويوثق الطباعة المتغيرة عبر `ESC i`، كما يذكر وضع Graphics عبر `ESC ( G` وMicroWeave عبر `ESC ( i` [1]. ويوضح مرجع Gutenprint أن تسلسل الطباعة يستخدم `ESC .` أو `ESC i`، وأن `ESC i` يحمل اللون، الضغط، عدد bits، عدد bytes لكل صف، وعدد الصفوف [2].

**الأثر:** يمكن أن ترفض L1800 البيانات، أو تعتبرها أوامر غير معروفة، أو تنتج صفحة فارغة/مشوهة. وجود `EncodedPage` ونجاح البناء لا يعني أن البايتات صالحة للطابعة.

**الإصلاح المطلوب:** إعادة كتابة Encoder حول `ESC ( G` ثم `ESC ( U` ثم `ESC ( K`/`ESC ( i` و`ESC i` لكل لون/كتلة حسب مواصفات L1800 الفعلية، مع اختبار hexdump مقابل ملف RAW صادر من درايفر Epson أو Gutenprint.

### F-02 — طول البيانات المعلن لا يطابق البيانات المرسلة

**الخطورة: حرجة.**  
**الموضع:** `ESCP2Encoder.cs:323-334, 349-397` و`589-610`.

الكود يعلن:

```csharp
int byteCount = width;
```

مع أن البيانات غير المضغوطة تُعبأ إلى `(width + 7) / 8` بايت. في اختبار بعرض 16 نقطة، سجّل الـ probe:

```text
header-declared-x=16
actual packed data before LF=2 bytes
```

حتى لو كان اختيار الأمر صحيحًا، إعلان 16 بينما البيانات الثنائية المعبأة 2 بايت ليس تعريفًا صحيحًا لنفس تنسيق الأمر. في `ESC i` يجب أن يكون حقل `BYTES` هو عدد البايتات في الصف، كما يوضح مرجع Gutenprint [2].

### F-03 — الضغط RLE مخصص وغير متوافق مع TIFF RLE في ESC/P2

**الخطورة: حرجة.**  
**الموضع:** `ESCP2Encoder.cs:323, 349-367, 399-430`.

الكود يستخدم `compressMode=33`، ثم يرسل ترميزًا خاصًا بالشكل `[0xFF, count, value]`. أما ESC/P2 الموثق فيستخدم أوضاع ضغط محددة، ومنها TIFF compression، ولها قواعد count مختلفة؛ مرجع Gutenprint يشرح أن البايت الأقل أو المساوي 128 يتبعه عدد معين من البايتات، وفوق 128 يعني تكرار قيمة واحدة [2]. هذه ليست الصيغة الموجودة في الريبو.

**الإصلاح:** استخدام TIFF RLE المطابق حرفيًا للموديل، أو تعطيل الضغط أثناء أول مرحلة تحقق. لا يجوز إبقاء زر الضغط مفعلًا مع Encoder غير متوافق.

### F-04 — إرسال VDS على ثلاث صفحات كاملة بدل إثبات interleaving للطابعة

**الخطورة: عالية.**  
**الموضع:** `RipPipeline.cs:278-280, 305-307, 729-750` و`ESCP2Encoder.cs:460-476, 479-514`.

`EncodePageVDS` ينشئ ثلاث `EncodedPage` منفصلة. كل صفحة تنتهي بـ `FORM_FEED`، ثم `SendPasses` يرسلها بالترتيب مع `Thread.Sleep`. هذا قد ينتج ثلاث صفحات أو ثلاث عمليات طباعة، وليس ثلاث تمريرات متداخلة على نفس موضع الورق. لا يوجد إثبات في الكود أن L1800 تفسر هذه الصفحات كـ passes لنفس الصورة.

**الإصلاح:** بناء blocks raster حقيقية داخل job واحد وفق نموذج الرأس والطابعة، أو الاعتماد على درايفر Epson/Gutenprint لإدارة interleaving. يجب أن يكون لكل pass تعريف واضح للون، الصفوف، الإزاحة الرأسية، وتغذية الورق.

### F-05 — قيمة `UseMultiPass` لا تتحكم فعليًا في القرار

**الخطورة: عالية.**  
**الموضع:** `RipPipeline.cs:262-281` و`MainViewModel.cs:404-405`.

التعليق يقول إن `UseMultiPass` خيار مستقل، لكن القرار الفعلي يعتمد على `LayoutComposer.QualityToResolution(_settings.QualityLevel)` فقط. في `BuildPipelineSettings` يتم ضبط `UseMultiPass = true` دائمًا، ولا توجد خانة واجهة مستقلة للتحكم فيه.

**الأثر:** المستخدم قد يختار جودة منخفضة أو إعدادًا يتوقع pass واحدًا، بينما سلوك الإرسال يحدده mapping الجودة فقط.

## 3. مشاكل محرك الألوان والميزات غير المكتملة

### F-06 — اسم ICC مضلل: التحويل الحقيقي لا يقرأ profile transform

**الخطورة: عالية.**  
**الموضع:** `IccColorEngine.cs:273-287, 304-318, 408-416`.

الكود يقرأ بعض bytes وtags من ملف ICC وينشئ `IccColorTransform`، لكن `TransformRGB` يطبق دوال رياضية داخلية فقط. التحويل الحقيقي لا يستخدم `_rgbTransform` إلا داخل `ApplyColorMode` عند وجود profile، بينما `RipPipeline.MapColorEngineSettings()` لا يمرر مسارات profiles أصلًا. كما أن `IccColorTransform` نفسه لا يفسر LUTs أو TRCs أو مصفوفات profile؛ بل يستخدم تحويل RGB/XYZ/Lab تقريبيًا.

لذلك الوصف الصحيح هو: **محرك RGB/XYZ/Lab تقريبي مع intent مبسط، وليس Color Management ICC كاملًا**. إدارة الألوان الأفضل يجب أن تعتمد على LittleCMS، وهو CMM مفتوح المصدر يدعم ICC v2/v4 وملفات متعددة الأحبار وsoftproofing، ومرخص MIT [3].

### F-07 — بعض Rendering Intents أسماء فقط

**الموضع:** `IccColorEngine.cs:161-167, 304-313`.

`Perception` و`PerceptionPhoto` و`ColorimetricAbsolute` و`ColorimetricRelative` تسقط عمليًا في نفس المسار العام، بينما `Saturation` و`PerceptionInksave` فقط لهما سلوك خاص. كذلك `PerceptionInksave` عبارة عن ضغط chroma ثابت `0.72`، ولا يمكن وصفها علميًا بأنها توفر دائمًا 20–35% حبر دون قياس على بروفايل وطابعة وورق محددين.

### F-08 — ColorMode الخاص بـ Sublimation وFilm لا يغير الكثافة كما يعلن

**الموضع:** `RipPipeline.cs:537-566`.

الـ mapping يربط StandardPhoto وVividPhoto وSublimation وFilmTransparent كلها إلى `ColorMode.ColorRGB`. يتم حساب متغير `density` مع زيادات 15% و25%، لكنه لا يُمرر إلى `ColorEngineSettings` ولا يُستخدم في هذا الموضع. الكثافة العامة تُطبق لاحقًا في `RipPipeline.cs:505-515`، لكنها لا تميز Sublimation عن Film.

**الحكم:** الميزتان موجودتان في القوائم والتعليقات، لكن السلوك الخاص بهما غير منفذ كما هو معلن.

### F-09 — D50/D65 وLAB ليست معالجة ICC مكتملة

**الموضع:** `IccColorEngine.cs:368-381, 424-452`.

توجد حسابات Lab، لكن مصفوفات adaptation غير مطبقة في المسار النهائي بالشكل الذي يجعل ReferenceLight نية جهاز كاملة. كما أن `LabL` يمر عبر `1.0 + value/100` ثم يُستخدم كمعامل، وهو تصميم خاص وليس محور LAB قياسيًا مباشرًا. يجب تثبيت semantics عبر اختبارات reference values ومقارنة LittleCMS.

## 4. مشاكل التحكم بالحبر وLc/Lm

### ما يعمل فعليًا

المسار التالي صحيح من ناحية منطقية:

```text
CheckBox القفل
→ EffectiveLightCyanGate / EffectiveLightMagentaGate
→ PipelineSettings
→ RipPipeline.ApplyColorEngineAndInkControl
→ ink8.Lc/Lm × gate
→ HalftoneResult
```

والاختبار الاصطناعي أثبت أن فتح القناة ينتج نقاطًا وأن القفل ينتج صفر نقاط للقناة المقفولة.

### F-10 — الإعداد الافتراضي يقفل Lm دون تنبيه واضح للمستخدم

**الموضع:** `MainViewModel.cs:259` و`PipelineSettings.cs/RipPipeline.cs:19`.

`LockLightMagenta = true` افتراضيًا، و`LightMagentaGate = 0.0` افتراضيًا. هذا قد يكون قرار ورشة مقصودًا، لكنه خطير لأن المستخدم قد يطبع دون Lm بينما يظن أن CMYKcc يعمل كاملًا. الأفضل أن يكون الافتراضي محفوظًا في preset واضح باسم مثل `Film/Sublimation — Lm Off`، أو عرض تحذير دائم في الواجهة.

### F-11 — إعدادات Bright Cyan/Bright Magenta ليست عناصر تحكم واجهة مستقلة

`InkControlSettings` يحتوي `BrightCyan` و`BrightMagenta`، لكن `BuildPipelineSettings` لا يمرر لهما قيمًا من UI. النسبة الموجودة في الواجهة (`LcInkPercent` و`LmInkPercent`) تعمل كـ gate بعد اشتقاق القنوات، وليست Bright factor نفسه. يجب توضيح الفرق للمستخدم أو إعادة تسمية الحقول حتى لا يعتقد أن النسبة تضبط منحنى light ink بينما هي تخفض القناة بعد اشتقاقها.

## 5. مشاكل النقل والاعتماد على العتاد

### F-12 — نجاح التشخيص لا يعني نجاح الطباعة

`UsbDiagnostics` يفحص وجود الجهاز أو الطابور، وقد يرسل job فارغًا إلى Spooler. هذا يثبت أن Windows فتح الطابور، لكنه لا يثبت أن RAW ESC/P2 صحيح، ولا أن L1800 طبعت الصورة.

### F-13 — LibUsbDotNet قديم بالنسبة لـ net8.0

`ElnagarPrint.RIP.Core.csproj` يستخدم `LibUsbDotNet 2.2.8` وتصدر NU1701. يجب اختبار بديل حديث أو عزل طبقة USB خلف interface واستخدام WinUSB/Native API موثق. لا يتم استبدال الدرايفر الرسمي بـ Zadig على جهاز الورشة قبل وجود مسار Spooler مؤكد.

### F-14 — لا يوجد إلغاء تعاوني فعلي داخل الـ pipeline

`AsyncPrintCommand` يمرر `CancellationToken` إلى `Task.Run`، لكن `RipPipeline.PrintImage` لا يستقبل token ولا يفحصه داخل حلقات تحميل البكسلات، الفصل، التنقيط، الترميز، أو `SendPasses`. لذلك زر الإلغاء لا يوقف مهمة بدأت بالفعل؛ قد يمنع فقط بدء مهمة جديدة أو يعود بعد اكتمال الخيط.

## 6. جودة الكود والبنية

### نقاط ضعف رئيسية

- `RipPipeline.cs` كبير جدًا ويجمع تحميل الصور، color management، ink control، layout، halftone، encoder، النقل، التكلفة، والسجل في class واحدة.
- لا توجد مشاريع اختبارات داخل الحل، ولا اختبارات xUnit/NUnit، ولا golden files للـ ESC/P2.
- توجد properties غير nullable بدون قيم ابتدائية في `HalftoneProcessor.cs`، وأعضاء USB غير nullable مثل `_writer` و`_reader` رغم أنها لا تُهيأ في constructor.
- `ESCP2Encoder.GetChannel` يعيد nullable من method غير nullable، وظهر ذلك في CS8603.
- `RipPipeline.cs:435-447` ينتج تحذيرات nullable عند قراءة `GetArea`; يجب التحقق من channel count قبل القراءة.
- `EstimateFromGrid` يمسك `catch` عامًا ويعيد null، ما يخفي عيوبًا حقيقية في حساب التكلفة.
- أسعار الحبر المعروضة في `TabConsumptionView.xaml` ثابتة TextBox وليست مرتبطة بـ model أو إعدادات محفوظة، رغم أن النص يقول إنها قابلة للتعديل.
- لا توجد ملفات profile فعلية للطابعة، ولا preset L1800 موثق يحتوي nozzle offsets، media mode، pass schedule، أو curves حقيقية.

## 7. الميزات: تعمل أم شكلية؟

### تعمل جزئيًا أو فعليًا

- اختيار صورة ومعاينة: يعمل في اختبار Core.
- RGB/grayscale في المعاينة: مرّ الاختبار الصناعي الحالي.
- قفل Lc وLm: يعمل على خرائط النقاط.
- سطوع/تباين/إشباع عامة: تغيّر الحسابات الرياضية، لكن يلزم اختبار بصري/لوني reference.
- Floyd–Steinberg: موجود فعليًا، مع مستويات 0–3.
- layout rotation/N-up/bleed: موجود على grid، لكن دلالات المقاس الفعلي وDPI تحتاج ربطًا بقياس الورق.
- Windows Spooler P/Invoke: موجود ككود، غير مختبر على Windows في هذه المراجعة.

### موجودة في الواجهة لكن غير مثبتة أو ناقصة

- طباعة Epson L1800 الفعلية: غير مثبتة.
- ICC intents السبعة: أغلبها aliases لمسار واحد.
- Sublimation/Film color modes: لا تطبق الزيادة المعلنة في الكثافة.
- VDS/Multi-pass: موجود كحزم منفصلة، لكن لا يوجد إثبات توافقها مع head timing للطابعة.
- Weave: ينشئ خريطة توزيع، لكن النمط `L1800_TriplePass()` ثابت وغير مستخرج من profile حقيقي للطابعة.
- أسعار الحبر: UI ثابتة وليست إعدادات قابلة للحفظ.
- إلغاء المهمة: غير تعاوني بعد بدء المعالجة.
- Smart drying: حساب برمجي تقريبي، وليس معايرة فعلية للورق والحبر والبيئة.

## 8. مشاريع مفتوحة المصدر مقترحة للدمج أو الاقتباس

### 1. Gutenprint

الرابط: https://gimp-print.sourceforge.io/  
الاستخدام المقترح: جعل `ESC/P2Encoder` مرجعًا للبنية الصحيحة لأوامر `ESC .` و`ESC i` وTIFF RLE وMicroWeave والـ color blocks. المرجع المنشور يشرح صيغة الأوامر، extended colors بما فيها Light Magenta=17 وLight Cyan=18، وتنسيق `BYTES` و`LINES` [2].

**القرار:** أفضل مصدر تقني لفهم البروتوكول، لكن لا تنسخ كودًا دون مراجعة الترخيص والتوافق مع C#.

### 2. `Ircama/epson_escp2`

الرابط: https://github.com/Ircama/epson_escp2  
الاستخدام المقترح: أداة مستقلة لإنتاج وفك وتحليل ESC/P2، مع دعم bit length 1/2/3 وRLE ومعاينة وdecoder. README يوضح أنه يستخدم `ESC i` لنقل raster، وأن الضغط والـ bit length جزء من صيغة الطباعة [4].

**القرار:** مناسب كـ oracle لاختبارات golden/hexdump وكأداة مقارنة، وليس دمجًا مباشرًا في تطبيق WPF بسبب اختلاف اللغة والترخيص والهدف.

### 3. Epson ESC/P2 Reference Manual

الرابط: https://files.support.epson.com/pdf/general/escp2ref.pdf  
الاستخدام المقترح: مصدر المواصفة عند بناء Encoder، خصوصًا `ESC (G`, `ESC (U`, `ESC (i`, `ESC .`, وقيود graphics mode [1].

**القرار:** ليس repo للدمج، لكنه مرجع إلزامي لاختبارات البروتوكول.

### 4. `endlessm/epson-inkjet-printer`

الرابط: https://github.com/endlessm/epson-inkjet-printer  
الاستخدام المقترح: دراسة فلاتر Epson/CUPS والتعامل مع عائلات الطابعات. المشروع مرخص LGPL-2.1، ويحتوي فلاتر ESC/P لطابعات Epson [5].

**القرار:** مناسب للاقتباس المعماري أو تشغيله كـ helper عبر CUPS/Linux، لكن لا يُدمج في Windows/WPF دون دراسة الترخيص والموديل.

### 5. Little CMS

الرابط: https://www.littlecms.com/color-engine/  
الاستخدام المقترح: استبدال parser/transform التقريبي في `IccColorEngine` بمحول ICC حقيقي، مع profile input/output وBPC وsoftproof وdevice separation. LittleCMS يذكر دعم ICC v2/v4 وملفات متعددة الأحبار وترخيص MIT [3].

**القرار:** أفضل دمج طويل المدى لمحرك الألوان، مع wrapper C/C++ أو binding موثوق لـ .NET، واختبارات مقارنة.

## 9. خطة الإصلاح حسب الأولوية

### P0 — قبل أي طباعة عميلة

1. إيقاف ادعاء «جاهز للطباعة» في README حتى نجاح اختبار hardware.
2. إعادة تصميم Encoder وفق `ESC i`/`ESC .` الحقيقي، لا `GS v 0`.
3. تعطيل RLE وVDS مؤقتًا حتى تطابق البيانات مواصفة L1800.
4. توليد RAW job صغير أبيض/أسود/CMYK/Lc/Lm ومقارنته بملف صالح من الدرايفر.
5. اختبار على طابعة L1800 حقيقية: nozzle check، صفحة ألوان أساسية، صورة، وقياس أن Lc/Lm المقفولين لا يخرجان نقاطًا.

### P1 — تثبيت المحرك

1. إضافة `ElnagarPrint.RIP.Core.Tests`.
2. اختبار `LightCyanGate=0` و`LightMagentaGate=0` على كل مرحلة، وليس preview فقط.
3. اختبارات golden للـ encoder مع parser/decoder.
4. إضافة cancellation token إلى كل حلقة كبيرة.
5. فصل `ImageLoader`, `ColorEngine`, `InkController`, `Halftoner`, `Layout`, `Encoder`, `Transport` بواجهات مستقلة.

### P2 — اللون والجودة

1. دمج LittleCMS أو binding موثوق.
2. إضافة ICC profiles فعلية للموديل والوسيط، مع preset لكل ورق.
3. فصل ColorMode behavior الحقيقي بدل mapping كل الأوضاع إلى `ColorRGB`.
4. معايرة Lc/Lm وUCR وTotal Ink Limit بطباعة charts وقياس.

### P3 — تجربة الورشة

1. حفظ presets مع version/schema validation.
2. ربط أسعار الحبر بالـ ViewModel وحفظها.
3. عرض تحذير واضح إذا كان Lm أو Lc مقفولًا.
4. إضافة سجل RAW job وhash للملف والإعدادات.
5. إضافة recovery/resume وحماية من إرسال job ناقص.

## 10. معيار قبول عملي للنسخة القادمة

لا تُعتبر النسخة جاهزة إلا بعد نجاح كل البنود التالية على Windows مع L1800:

- بناء الحل دون أخطاء ودون NU1701 أو مع قرار موثق لمعالجته.
- طباعة صفحة اختبار CMYK وLc وLm صحيحة.
- فتح Lc ينتج نقاطًا في الملف والورقة، وقفل Lc ينتج صفرًا في كل من الملف والورقة.
- فتح Lm ينتج نقاطًا، وقفل Lm ينتج صفرًا.
- تجربة 360/720 dpi، ضغط تشغيل/إيقاف، uni/bi، وصورة كبيرة.
- إلغاء أثناء ملف كبير يوقف المعالجة والإرسال فعلًا.
- مقارنة استهلاك الحبر المتوقع مع قياس تعبئة/وزن أو chart معايرة، لا اعتبار التقدير رقمًا دقيقًا تلقائيًا.
- حفظ hexdump للـ RAW job ونتيجة الاختبار في `docs/hardware-validation/`.

## 11. الإصلاحات المنفذة في هذه النسخة

بعد التقرير الأولي تم تنفيذ إصلاحات داخل الريبو، مع إبقاء التحقق الميداني على Epson L1800 شرطًا منفصلًا:

- استبدال مسار `GS v`/`ESC *` غير المناسب بمسار ESC/P2 يبدأ بـ `ESC (G` ويستخدم `ESC i` لكل قناة raster.
- استخدام ألوان ESC/P2 الموسعة للقنوات: K=0 وM=1 وC=2 وY=4 وLm=17 وLc=18.
- تصحيح حساب `BYTES` ليكون عدد البايتات المعبأة فعلًا للصف، بدل إعلان عرض البكسلات.
- استبدال RLE المخصص بترميز TIFF RLE القياسي عند تفعيل الضغط.
- تصحيح أوامر وحدة القياس والاتجاه إلى صيغ `ESC (U` و`ESC U`، وتفعيل Graphics/MicroWeave بالأوامر المقابلة.
- تمرير `CancellationToken` إلى `RipPipeline` وفحصه داخل فصل الألوان وبين النسخ والوجهين.
- جعل `UseMultiPass` مؤثرًا فعليًا في قرار استخدام VDS بدل تجاهله.
- إضافة هذا التقرير إلى الريبو نفسه.

تمت إعادة البناء بعد التعديلات بنجاح: **0 أخطاء**، مع بقاء تحذيري `NU1701` الخاصين بـ `LibUsbDotNet 2.2.8`. اختبار Core ظل يثبت قفل Lc/Lm: القناة المفتوحة تنتج نقاطًا، والمقفولة تنتج صفرًا. هذه الإصلاحات ترفع صحة الـ byte stream، لكنها لا تعادل اعتمادًا ميدانيًا نهائيًا؛ يجب اختبار RAW job على L1800 وتوثيق النتيجة قبل الطباعة التجارية.

## References

[1]: https://files.support.epson.com/pdf/general/escp2ref.pdf "EPSON ESC/P Reference Manual"
[2]: https://gimp-print.sourceforge.io/reference-html/c463.html "Gutenprint ESC/P2 Developer Reference"
[3]: https://www.littlecms.com/color-engine/ "Little CMS Open Source Color Engine"
[4]: https://github.com/Ircama/epson_escp2 "Ircama epson_escp2"
[5]: https://github.com/endlessm/epson-inkjet-printer "Endlessm Epson Inkjet Printer"
