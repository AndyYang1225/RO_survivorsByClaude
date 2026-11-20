# 純圖像判斷：視覺框架（Plan 1／5）Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 exe 端建好「不碰網頁」的視覺自動化基礎：截圖、像素色塊、範本比對、繁中 OCR、瀏覽器層級的真實輸入，以及一個用來校正這些功能的「視覺除錯」視窗。

**Architecture:** exe 用 PrintWindow 截下 WebView2 的畫面（原計畫用 `CapturePreviewAsync`，實測太慢，見文末「實機驗證結果」），轉成純資料 `Frame`（BGRA byte[]）。所有判斷（色塊、範本、OCR）都只吃 `Frame`，可以寫單元測試。操作透過 DevTools Protocol 的 `Input.dispatchMouseEvent`／`Input.dispatchKeyEvent` 送出，這是瀏覽器層級的輸入，網頁收到的事件跟實體滑鼠一樣（`isTrusted = true`），而且**不會在頁面執行任何 JS**。這份計畫不改動現有的 JS 引擎，舊的自動化照常可用；新框架先以「除錯視窗」的形式上線。

**Tech Stack:** .NET 8 WinForms（C# 12）、Microsoft.Web.WebView2 1.0.2903.40、Windows.Media.Ocr（WinRT，透過 `net8.0-windows10.0.19041.0` TFM 取得）、xUnit 2.9

## 整體路線（5 份計畫，這份是第 1 份）

| 計畫 | 內容 | 完成後可用的東西 |
|---|---|---|
| **1. 視覺框架（本份）** | Frame、色塊、範本比對、OCR、CDP 輸入、視覺除錯視窗 | 除錯視窗：看即時截圖、測點擊、框選 OCR、存範本、找範本 |
| 2. 視覺模式＋流程按鈕 | 規則清單（RULES）搬到 C#、`app.json` 的 `VisionMode` 開關（開啟時完全不注入 JS）、F8／F9 改由 exe 處理、單人大廳／開始遊戲／前往野外／返回角色／按升級／遠征 NPC | 視覺模式下能自動開局、結算、回城 |
| 3. 選卡 | OCR 讀卡名、稀有度、「新！」標記、放逐／專注／重抽剩餘次數；`decide()` 移植成 C# 並寫測試 | 視覺模式下自動選卡 |
| 4. 野外走位 | 小地圖色塊（自己、磁鐵、寶箱、王的掉落物、王）、躲鐘、扭蛋、繞圈、卡住偵測 | 視覺模式下自動走位 |
| 5. 收尾 | 尋寶率／神祇（真滑鼠 hover＋OCR 說明框）、對局紀錄（OCR 結算畫面）、移除 JS 注入與 Gist 引擎更新器 | 完全不碰網頁的版本 |

計畫 2～5 要用到實際遊戲畫面的範本和截圖，等這份做完、用除錯視窗收集好素材後再逐份寫。

## Global Constraints

- 主專案維持 .NET 8、C# 12；TFM 改為 `net8.0-windows10.0.19041.0`（OCR 需要 WinRT），`SupportedOSPlatformVersion` 設為 `10.0.17763.0`。
- 主專案**不新增任何 NuGet 套件**：維持單一 exe 的大小。刻意不用 OpenCV（OpenCvSharp 原生 dll 約 60MB）。
- `Vision/` 底下的程式碼**不得**呼叫 `ExecuteScriptAsync`、`AddScriptToExecuteOnDocumentCreatedAsync`、`PostWebMessage*`，也不得用 CDP 的 `Runtime.*`／`DOM.*`。只允許截圖和 `Input.*`。
- 這份計畫不修改 `Engine/engine.js`，也不修改 `EngineBridge`。
- 程式碼風格依 `coding-style` skill：識別字用英文；註解、XML doc、log 用繁體中文；public class 都要有 `<summary>`；單一語句的 if 不加大括號。
- 測試專案放在 `C:\Ro\RoHelper.Tests`（跟 `RoHelper` 平行）。不能放在 `RoHelper\` 底下，否則主專案預設的 `**/*.cs` 會把測試檔一起編譯進 exe。
- 建置前要先關掉正在執行的 RoHelper.exe，否則輸出檔被鎖住會建置失敗。
- 專案目前不是 git repo，所以每個 Task 的檢查點是「建置＋測試全綠」。要版本控制的話，開始前先在 `C:\Ro` 執行 `git init`，並在每個檢查點 commit。

---

## 檔案結構

| 檔案 | 職責 |
|---|---|
| `RoHelper/Vision/Frame.cs` | 截圖像素（BGRA）與 Bitmap 互轉、裁切、縮放、灰階快取 |
| `RoHelper/Vision/ViewportMapper.cs` | 截圖像素座標 → 網頁 CSS 座標 |
| `RoHelper/Vision/ColorBlobs.cs` | 依顏色條件找色塊（移植自 JS 的 `clusters`／`scanBalls` 寫法） |
| `RoHelper/Vision/TemplateMatcher.cs` | 灰階範本比對（粗找＋精找） |
| `RoHelper/Vision/TemplateLibrary.cs` | 範本 PNG＋設定 JSON 的讀寫，依 dpr 縮放後比對 |
| `RoHelper/Vision/OcrReader.cs` | Windows 繁中 OCR 包裝 |
| `RoHelper/Vision/ScreenGrabber.cs` | WebView2 截圖 → Frame |
| `RoHelper/Vision/CdpInput.cs` | CDP 真實滑鼠／滾輪／鍵盤輸入 |
| `RoHelper/Vision/VisionKit.cs` | 把上面幾個組在一起，給除錯視窗（以及之後的視覺引擎）使用 |
| `RoHelper/Forms/ZoomMapper.cs` | PictureBox Zoom 模式的座標換算 |
| `RoHelper/Forms/VisionDebugForm.cs` | 視覺除錯視窗 |
| `RoHelper/Forms/GameForm.cs`（修改） | 選單加「視覺除錯」 |
| `RoHelper/AppPaths.cs`（修改） | 加 `TemplatesDir` |
| `RoHelper/RoHelper.csproj`（修改） | TFM 改成 Windows 10 版本 |
| `RoHelper.Tests/*` | 單元測試 |

---

### Task 1: 測試專案、TFM 切換、Frame、ViewportMapper

**Files:**
- Modify: `C:\Ro\RoHelper\RoHelper.csproj`
- Create: `C:\Ro\RoHelper\Vision\Frame.cs`
- Create: `C:\Ro\RoHelper\Vision\ViewportMapper.cs`
- Create: `C:\Ro\RoHelper.Tests\RoHelper.Tests.csproj`
- Create: `C:\Ro\RoHelper.Tests\TestFrames.cs`
- Create: `C:\Ro\RoHelper.Tests\FrameTests.cs`

**Interfaces:**
- Produces:
  - `Frame(int width, int height, byte[] pixels)`；`int Width`、`int Height`、`byte[] Pixels`（BGRA）、`byte[] Gray`（延遲計算並快取）
  - `(byte R, byte G, byte B) Frame.At(int x, int y)`
  - `static Frame Frame.FromBitmap(Bitmap bmp)`、`Bitmap Frame.ToBitmap()`
  - `Frame Frame.Crop(Rectangle r)`（超出邊界自動裁掉）、`Frame Frame.Resize(double scale)`（最近鄰）
  - `readonly record struct ViewportMapper(double Dpr)`；`(double X, double Y) ToCss(double x, double y)`
  - 測試輔助 `TestFrames.Solid(int w, int h, byte r, byte g, byte b)`、`TestFrames.Fill(Frame f, Rectangle rect, byte r, byte g, byte b)`、`TestFrames.Pattern(int w, int h)`

- [ ] **Step 1: 改主專案 TFM**

`C:\Ro\RoHelper\RoHelper.csproj` 第一個 PropertyGroup 裡，把
```xml
    <TargetFramework>net8.0-windows</TargetFramework>
```
換成
```xml
    <!-- 指定 Windows 10 版本才拿得到 WinRT API（Windows.Media.Ocr，視覺判斷的 OCR 用） -->
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
```

- [ ] **Step 2: 建立測試專案**

`C:\Ro\RoHelper.Tests\RoHelper.Tests.csproj`：
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <SupportedOSPlatformVersion>10.0.17763.0</SupportedOSPlatformVersion>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RoHelper\RoHelper.csproj" />
  </ItemGroup>

</Project>
```

`C:\Ro\RoHelper.Tests\TestFrames.cs`：
```csharp
using RoHelper.Vision;

namespace RoHelper.Tests;

/// <summary>
/// 測試用的假截圖。alpha 一律 255：Frame 轉 Bitmap 給 OCR 時，alpha 0 會變成全黑。
/// </summary>
public static class TestFrames
{
    public static Frame Solid(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < px.Length; i += 4)
        {
            px[i] = b;
            px[i + 1] = g;
            px[i + 2] = r;
            px[i + 3] = 255;
        }
        return new Frame(w, h, px);
    }

    public static void Fill(Frame f, Rectangle rect, byte r, byte g, byte b)
    {
        for (var y = rect.Top; y < rect.Bottom; y++)
            for (var x = rect.Left; x < rect.Right; x++)
            {
                var i = (y * f.Width + x) * 4;
                f.Pixels[i] = b;
                f.Pixels[i + 1] = g;
                f.Pixels[i + 2] = r;
                f.Pixels[i + 3] = 255;
            }
    }

    /// <summary>
    /// 平滑的漸層加兩塊實心方塊。範本比對的粗找每 2 點試一次，背景要平滑，差一點的位置分數才會接近；
    /// 純雜訊的圖差一點就跟隨機位置一樣差，粗找會找錯地方（真實的遊戲 UI 是平滑的，不會有這個問題）。
    /// </summary>
    public static Frame Pattern(int w = 200, int h = 100)
    {
        var f = Solid(w, h, 0, 0, 0);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var v = (byte)((x * 2 + y * 3) % 256);
                var i = (y * w + x) * 4;
                f.Pixels[i] = v;
                f.Pixels[i + 1] = v;
                f.Pixels[i + 2] = v;
            }
        Fill(f, new Rectangle(60, 35, 10, 8), 255, 255, 255);
        Fill(f, new Rectangle(20, 70, 15, 10), 0, 0, 0);
        return f;
    }
}
```

- [ ] **Step 3: 寫會失敗的測試**

`C:\Ro\RoHelper.Tests\FrameTests.cs`：
```csharp
using RoHelper.Vision;

namespace RoHelper.Tests;

public class FrameTests
{
    [Fact]
    public void At_ReadsRgbFromBgra()
    {
        var f = TestFrames.Solid(4, 3, 10, 20, 30);

        Assert.Equal(((byte)10, (byte)20, (byte)30), f.At(3, 2));
    }

    [Fact]
    public void Crop_CopiesRegion_AndClipsToBounds()
    {
        var f = TestFrames.Solid(10, 10, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(8, 8, 2, 2), 255, 0, 0);

        var c = f.Crop(new Rectangle(7, 7, 10, 10));

        Assert.Equal(3, c.Width);
        Assert.Equal(3, c.Height);
        Assert.Equal(((byte)0, (byte)0, (byte)0), c.At(0, 0));
        Assert.Equal(((byte)255, (byte)0, (byte)0), c.At(2, 2));
    }

    [Fact]
    public void Resize_Doubles_WithNearestNeighbor()
    {
        var f = TestFrames.Solid(2, 1, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(1, 0, 1, 1), 255, 255, 255);

        var r = f.Resize(2);

        Assert.Equal(4, r.Width);
        Assert.Equal(2, r.Height);
        Assert.Equal(((byte)0, (byte)0, (byte)0), r.At(1, 1));
        Assert.Equal(((byte)255, (byte)255, (byte)255), r.At(2, 1));
    }

    [Fact]
    public void BitmapRoundTrip_KeepsPixels()
    {
        var f = TestFrames.Pattern(30, 20);

        using var bmp = f.ToBitmap();
        var back = Frame.FromBitmap(bmp);

        Assert.Equal(f.Pixels, back.Pixels);
    }

    [Fact]
    public void Gray_UsesLuma()
    {
        var f = TestFrames.Solid(1, 1, 255, 0, 0);

        Assert.Equal(76, f.Gray[0]);
    }

    [Fact]
    public void ViewportMapper_DividesByDpr()
    {
        var m = new ViewportMapper(1.25);

        Assert.Equal((200d, 80d), m.ToCss(250, 100));
    }
}
```

- [ ] **Step 4: 執行測試，確認失敗**

Run: `cd /c/Ro && dotnet test RoHelper.Tests`
Expected: 編譯失敗，訊息含 `The type or namespace name 'Vision' does not exist`

- [ ] **Step 5: 實作 Frame**

`C:\Ro\RoHelper\Vision\Frame.cs`：
```csharp
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RoHelper.Vision;

/// <summary>
/// 一張截圖的像素：BGRA，每點 4 bytes，記憶體排列跟 GDI+ 的 Format32bppArgb 相同。
/// 刻意不直接拿 Bitmap 做判斷：Bitmap 每次讀像素都要 LockBits，也不是執行緒安全；
/// 判斷邏輯只吃這個純資料，才能用假圖寫單元測試。
/// </summary>
public sealed class Frame
{
    private byte[]? _gray;

    public Frame(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height * 4)
            throw new ArgumentException("像素陣列長度跟寬高不符", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRA 像素。測試可以直接改；改了之後不要再讀 <see cref="Gray"/>（已快取的灰階不會更新）</summary>
    public byte[] Pixels { get; }

    /// <summary>灰階（每點 1 byte），第一次讀時計算後快取：同一張截圖常要比對好幾個範本</summary>
    public byte[] Gray => _gray ??= ComputeGray();

    public (byte R, byte G, byte B) At(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Pixels[i + 2], Pixels[i + 1], Pixels[i]);
    }

    public static Frame FromBitmap(Bitmap bmp)
    {
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = bmp.Width * 4;
            var px = new byte[row * bmp.Height];
            for (var y = 0; y < bmp.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, px, y * row, row);
            return new Frame(bmp.Width, bmp.Height, px);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = Width * 4;
            for (var y = 0; y < Height; y++)
                Marshal.Copy(Pixels, y * row, data.Scan0 + y * data.Stride, row);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    /// <summary>裁切。超出畫面的部分自動裁掉；完全在畫面外時回傳 0×0 的 Frame</summary>
    public Frame Crop(Rectangle r)
    {
        r = Rectangle.Intersect(r, new Rectangle(0, 0, Width, Height));
        var row = r.Width * 4;
        var px = new byte[row * r.Height];
        for (var y = 0; y < r.Height; y++)
            Buffer.BlockCopy(Pixels, ((r.Y + y) * Width + r.X) * 4, px, y * row, row);
        return new Frame(r.Width, r.Height, px);
    }

    /// <summary>
    /// 最近鄰縮放。範本比對要求「同一個來源像素放大後仍是同一個值」，用內插會把邊緣糊掉、分數變低；
    /// OCR 需要平滑放大，由 <see cref="OcrReader"/> 自己用 GDI+ 的 bicubic 處理。
    /// </summary>
    public Frame Resize(double scale)
    {
        var w = Math.Max(1, (int)Math.Round(Width * scale));
        var h = Math.Max(1, (int)Math.Round(Height * scale));
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        {
            var sy = Math.Min(Height - 1, (int)(y / scale));
            for (var x = 0; x < w; x++)
            {
                var sx = Math.Min(Width - 1, (int)(x / scale));
                Buffer.BlockCopy(Pixels, (sy * Width + sx) * 4, px, (y * w + x) * 4, 4);
            }
        }
        return new Frame(w, h, px);
    }

    // 整數版的 Rec.601 亮度：0.299 R + 0.587 G + 0.114 B
    private byte[] ComputeGray()
    {
        var g = new byte[Width * Height];
        for (int i = 0, p = 0; i < g.Length; i++, p += 4)
            g[i] = (byte)((Pixels[p + 2] * 299 + Pixels[p + 1] * 587 + Pixels[p] * 114) / 1000);
        return g;
    }
}
```

`C:\Ro\RoHelper\Vision\ViewportMapper.cs`：
```csharp
namespace RoHelper.Vision;

/// <summary>
/// 截圖像素座標 → 網頁 CSS 座標。截圖是實體像素，CDP 的輸入座標是 CSS 像素，兩者差一個 dpr
/// （= Windows 顯示縮放 × 頁面縮放，例如 125% 縮放時 1.25）。
/// dpr 由呼叫端提供（WebView2 控制項的 DeviceDpi／96 × ZoomFactor），刻意不從網頁讀 window.devicePixelRatio：那要在頁面執行 JS。
/// </summary>
public readonly record struct ViewportMapper(double Dpr)
{
    public (double X, double Y) ToCss(double x, double y) => (x / Dpr, y / Dpr);
}
```

- [ ] **Step 6: 執行測試，確認通過**

Run: `cd /c/Ro && dotnet test RoHelper.Tests`
Expected: `Passed!  - Failed: 0, Passed: 6`

- [ ] **Step 7: 確認主專案仍可建置**

Run: `cd /c/Ro && dotnet build RoHelper/RoHelper.csproj`
Expected: `Build succeeded.`，0 個 error。輸出資料夾會變成 `bin\Debug\net8.0-windows10.0.19041.0\`（正常現象）。

- [ ] **Step 8: 檢查點**（有 git 的話：`git add -A && git commit -m "feat(vision): Frame、ViewportMapper 與測試專案"`）

---

### Task 2: ColorBlobs 色塊偵測

**Files:**
- Create: `C:\Ro\RoHelper\Vision\ColorBlobs.cs`
- Test: `C:\Ro\RoHelper.Tests\ColorBlobsTests.cs`

**Interfaces:**
- Consumes: `Frame`（Task 1）
- Produces:
  - `sealed class Blob`：`int X0, Y0, X1, Y1`（含端點）、`int Count`、`int Width`、`int Height`、`Point Center`
  - `static List<Blob> ColorBlobs.Find(Frame f, Rectangle region, Func<byte, byte, byte, bool> match, int step = 1, int gap = 3)`，`match` 的參數是 (r, g, b)

- [ ] **Step 1: 寫會失敗的測試**

`C:\Ro\RoHelper.Tests\ColorBlobsTests.cs`：
```csharp
using RoHelper.Vision;

namespace RoHelper.Tests;

public class ColorBlobsTests
{
    private static bool IsRed(byte r, byte g, byte b) => r > 200 && g < 50 && b < 50;

    [Fact]
    public void Find_SeparatesTwoRedSquares()
    {
        var f = TestFrames.Solid(100, 100, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(10, 10, 8, 8), 255, 0, 0);
        TestFrames.Fill(f, new Rectangle(50, 60, 6, 6), 255, 0, 0);

        var blobs = ColorBlobs.Find(f, new Rectangle(0, 0, 100, 100), IsRed, step: 1, gap: 2);

        Assert.Equal(2, blobs.Count);
        var a = blobs.Single(b => b.X0 == 10);
        Assert.Equal((10, 17, 17, 64), (a.Y0, a.X1, a.Y1, a.Count));
        var c = blobs.Single(b => b.X0 == 50);
        Assert.Equal((60, 55, 65, 36), (c.Y0, c.X1, c.Y1, c.Count));
        Assert.Equal(new Point(52, 62), c.Center);
    }

    [Fact]
    public void Find_OnlyLooksInsideRegion()
    {
        var f = TestFrames.Solid(100, 100, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(10, 10, 8, 8), 255, 0, 0);
        TestFrames.Fill(f, new Rectangle(50, 60, 6, 6), 255, 0, 0);

        var blobs = ColorBlobs.Find(f, new Rectangle(40, 40, 60, 60), IsRed);

        Assert.Single(blobs);
        Assert.Equal(50, blobs[0].X0);
    }

    [Fact]
    public void Find_WithStep_SamplesEveryNthPixel()
    {
        var f = TestFrames.Solid(100, 100, 0, 0, 0);
        TestFrames.Fill(f, new Rectangle(10, 10, 8, 8), 255, 0, 0);

        var blobs = ColorBlobs.Find(f, new Rectangle(0, 0, 100, 100), IsRed, step: 2);

        Assert.Single(blobs);
        Assert.Equal(16, blobs[0].Count);
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter ColorBlobsTests`
Expected: 編譯失敗，訊息含 `The name 'ColorBlobs' does not exist`

- [ ] **Step 3: 實作**

`C:\Ro\RoHelper\Vision\ColorBlobs.cs`：
```csharp
namespace RoHelper.Vision;

/// <summary>一塊符合顏色條件的區域（座標都是截圖像素，X1／Y1 含端點）</summary>
public sealed class Blob
{
    internal Blob(int x, int y)
    {
        X0 = X1 = x;
        Y0 = Y1 = y;
    }

    public int X0 { get; private set; }
    public int Y0 { get; private set; }
    public int X1 { get; private set; }
    public int Y1 { get; private set; }

    /// <summary>命中的取樣點數（step 大於 1 時是取樣後的點數，不是實際像素數）</summary>
    public int Count { get; private set; }

    public int Width => X1 - X0 + 1;
    public int Height => Y1 - Y0 + 1;
    public Point Center => new((X0 + X1) / 2, (Y0 + Y1) / 2);

    internal void Add(int x, int y)
    {
        Count++;
        X0 = Math.Min(X0, x);
        Y0 = Math.Min(Y0, y);
        X1 = Math.Max(X1, x);
        Y1 = Math.Max(Y1, y);
    }
}

/// <summary>
/// 依顏色條件找色塊：小地圖的磁鐵／寶箱／自己、3D 畫面的扭蛋和鐘都用這個。
/// 演算法照 JS 版（scanBalls、scanBell）：逐點掃，命中的點如果落在既有色塊外框 gap 格以內就併進去，否則開新的一塊。
/// 刻意不做完整的連通元件標記：目標都是小而分散的色塊，這個寫法夠用，而且結果跟 JS 版一致，移植判斷門檻時才對得起來。
/// </summary>
public static class ColorBlobs
{
    /// <param name="step">取樣間隔：3 表示每 3 點取 1 點（相當於 JS 版把畫面縮成 1/3）</param>
    /// <param name="gap">距離既有色塊外框幾個取樣格以內算同一塊</param>
    public static List<Blob> Find(Frame f, Rectangle region, Func<byte, byte, byte, bool> match, int step = 1, int gap = 3)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, f.Width, f.Height));
        var blobs = new List<Blob>();
        var px = f.Pixels;
        var reach = gap * step;
        for (var y = region.Top; y < region.Bottom; y += step)
            for (var x = region.Left; x < region.Right; x += step)
            {
                var i = (y * f.Width + x) * 4;
                if (!match(px[i + 2], px[i + 1], px[i]))
                    continue;
                var blob = blobs.Find(b => x >= b.X0 - reach && x <= b.X1 + reach && y >= b.Y0 - reach && y <= b.Y1 + reach);
                if (blob is null)
                {
                    blob = new Blob(x, y);
                    blobs.Add(blob);
                }
                blob.Add(x, y);
            }
        return blobs;
    }
}
```

注意：`new Blob(x, y)` 之後緊接著 `Add(x, y)`，所以第一個點只會算一次（建構子只設外框、不加 Count）。

- [ ] **Step 4: 執行測試，確認通過**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter ColorBlobsTests`
Expected: `Passed!  - Failed: 0, Passed: 3`

- [ ] **Step 5: 檢查點**（有 git：`git commit -am "feat(vision): ColorBlobs 色塊偵測"`，新檔先 `git add`）

---

### Task 3: TemplateMatcher 範本比對

**Files:**
- Create: `C:\Ro\RoHelper\Vision\TemplateMatcher.cs`
- Test: `C:\Ro\RoHelper.Tests\TemplateMatcherTests.cs`

**Interfaces:**
- Consumes: `Frame.Gray`、`Frame.Crop`（Task 1）、`TestFrames.Pattern`（Task 1）
- Produces:
  - `readonly record struct MatchResult(Rectangle Bounds, double Score)`：Score 介於 0～1，1 代表完全相同
  - `static MatchResult? TemplateMatcher.Find(Frame frame, Frame template, Rectangle region)`：範本比搜尋範圍大時回 null

- [ ] **Step 1: 寫會失敗的測試**

`C:\Ro\RoHelper.Tests\TemplateMatcherTests.cs`：
```csharp
using RoHelper.Vision;

namespace RoHelper.Tests;

public class TemplateMatcherTests
{
    [Fact]
    public void Find_LocatesExactCrop_AtOddPosition()
    {
        var f = TestFrames.Pattern();
        var t = f.Crop(new Rectangle(57, 31, 30, 20));

        var m = TemplateMatcher.Find(f, t, new Rectangle(0, 0, f.Width, f.Height));

        Assert.NotNull(m);
        Assert.Equal(new Rectangle(57, 31, 30, 20), m.Value.Bounds);
        Assert.Equal(1.0, m.Value.Score, 3);
    }

    [Fact]
    public void Find_StaysInsideRegion()
    {
        var f = TestFrames.Pattern();
        var t = f.Crop(new Rectangle(57, 31, 30, 20));

        var m = TemplateMatcher.Find(f, t, new Rectangle(100, 0, 100, 100));

        Assert.NotNull(m);
        Assert.True(m.Value.Bounds.X >= 100);
        Assert.True(m.Value.Score < 1.0);
    }

    [Fact]
    public void Find_ReturnsNull_WhenTemplateLargerThanRegion()
    {
        var f = TestFrames.Pattern();
        var t = f.Crop(new Rectangle(0, 0, 50, 50));

        Assert.Null(TemplateMatcher.Find(f, t, new Rectangle(0, 0, 40, 40)));
    }

    [Fact]
    public void Find_ScoresZero_ForOppositeColors()
    {
        var f = TestFrames.Solid(50, 50, 0, 0, 0);
        var t = TestFrames.Solid(10, 10, 255, 255, 255);

        var m = TemplateMatcher.Find(f, t, new Rectangle(0, 0, 50, 50));

        Assert.NotNull(m);
        Assert.Equal(0.0, m.Value.Score, 3);
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter TemplateMatcherTests`
Expected: 編譯失敗，訊息含 `The name 'TemplateMatcher' does not exist`

- [ ] **Step 3: 實作**

`C:\Ro\RoHelper\Vision\TemplateMatcher.cs`：
```csharp
namespace RoHelper.Vision;

/// <summary>範本比對結果。Score＝1 − 平均灰階差／255，1 代表完全一樣</summary>
public readonly record struct MatchResult(Rectangle Bounds, double Score);

/// <summary>
/// 在截圖的指定範圍裡找跟範本最像的位置（灰階、平均絕對差）。
/// 步驟 1：粗找，每 2 點試一個位置、範本每 2 點取 1 點。
/// 步驟 2：精找，在粗找最佳點的 ±2 範圍逐點、範本逐點重算。
/// 刻意不用 OpenCV：OpenCvSharp 的原生 dll 約 60MB，單一 exe 會大很多；搜尋範圍都是按鈕、卡片框這種小區域，純 C# 就夠快。
/// 刻意只比灰階：按鈕的形狀和文字才是辨識重點；需要分辨顏色（例如按鈕反灰）時，由呼叫端另外檢查顏色。
/// </summary>
public static class TemplateMatcher
{
    public static MatchResult? Find(Frame frame, Frame template, Rectangle region)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
        if (template.Width == 0 || template.Height == 0 || template.Width > region.Width || template.Height > region.Height)
            return null;

        var f = frame.Gray;
        var t = template.Gray;
        var maxX = region.Right - template.Width;
        var maxY = region.Bottom - template.Height;

        // 步驟 1：粗找
        var (cx, cy, cd) = (region.X, region.Y, double.MaxValue);
        for (var y = region.Y; y <= maxY; y += 2)
            for (var x = region.X; x <= maxX; x += 2)
            {
                var d = Diff(f, frame.Width, t, template.Width, template.Height, x, y, 2, cd);
                if (d < cd)
                    (cx, cy, cd) = (x, y, d);
            }

        // 步驟 2：精找（取樣密度不同，分數不能跟粗找比，所以重新從最大值開始）
        var (bx, by, bd) = (cx, cy, double.MaxValue);
        for (var y = Math.Max(region.Y, cy - 2); y <= Math.Min(maxY, cy + 2); y++)
            for (var x = Math.Max(region.X, cx - 2); x <= Math.Min(maxX, cx + 2); x++)
            {
                var d = Diff(f, frame.Width, t, template.Width, template.Height, x, y, 1, bd);
                if (d < bd)
                    (bx, by, bd) = (x, y, d);
            }

        return new MatchResult(new Rectangle(bx, by, template.Width, template.Height), 1 - bd / 255);
    }

    /// <summary>
    /// 平均絕對差。累積的差已經超過 limit（目前最佳）就提早回傳 MaxValue：這個位置不可能更好了，省下大部分的計算。
    /// </summary>
    private static double Diff(byte[] f, int fw, byte[] t, int tw, int th, int ox, int oy, int sample, double limit)
    {
        long total = (long)((tw + sample - 1) / sample) * ((th + sample - 1) / sample);
        var cap = limit == double.MaxValue ? long.MaxValue : (long)(limit * total);
        long sum = 0;
        for (var y = 0; y < th; y += sample)
        {
            var fr = (oy + y) * fw + ox;
            var tr = y * tw;
            for (var x = 0; x < tw; x += sample)
                sum += Math.Abs(f[fr + x] - t[tr + x]);
            if (sum > cap)
                return double.MaxValue;
        }
        return (double)sum / total;
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter TemplateMatcherTests`
Expected: `Passed!  - Failed: 0, Passed: 4`

- [ ] **Step 5: 檢查點**（有 git：`git add -A && git commit -m "feat(vision): TemplateMatcher 範本比對"`）

---

### Task 4: TemplateLibrary 範本庫

**Files:**
- Modify: `C:\Ro\RoHelper\AppPaths.cs`（加 `TemplatesDir`）
- Create: `C:\Ro\RoHelper\Vision\TemplateLibrary.cs`
- Test: `C:\Ro\RoHelper.Tests\TemplateLibraryTests.cs`

**Interfaces:**
- Consumes: `Frame`、`TemplateMatcher.Find`、`MatchResult`（Task 1、3）；`JsonFile.Options`、`JsonFile.WriteText`（既有 `Storage/JsonFile.cs`）
- Produces:
  - `AppPaths.TemplatesDir`（`%AppData%\RoHelper\templates`）
  - `sealed record TemplateInfo(string Name, Frame Image, RectangleF Region, double Threshold, double Dpr)`：Region 是佔整張截圖的比例（0～1）
  - `TemplateLibrary(string dir)`；`const double DefaultThreshold = 0.9`
  - `IReadOnlyCollection<string> Names`、`TemplateInfo? Get(string name)`、`void Reload()`
  - `void Save(string name, Frame image, RectangleF region, double threshold, double dpr)`：名稱有不合法字元時丟 `ArgumentException`
  - `MatchResult? Match(Frame frame, string name, double dpr)`：不看門檻，回傳最佳位置（除錯用）
  - `MatchResult? Find(Frame frame, string name, double dpr)`：分數達到門檻才回傳

- [ ] **Step 1: 寫會失敗的測試**

`C:\Ro\RoHelper.Tests\TemplateLibraryTests.cs`：
```csharp
using RoHelper.Vision;

namespace RoHelper.Tests;

public sealed class TemplateLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "RoHelperTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void SaveThenReload_FindsTemplate()
    {
        var f = TestFrames.Pattern();
        new TemplateLibrary(_dir).Save("白塊", f.Crop(new Rectangle(57, 31, 30, 20)), new RectangleF(0, 0, 1, 1), 0.9, 1.0);

        var lib = new TemplateLibrary(_dir);
        var m = lib.Find(f, "白塊", 1.0);

        Assert.Contains("白塊", lib.Names);
        Assert.NotNull(m);
        Assert.Equal(new Rectangle(57, 31, 30, 20), m.Value.Bounds);
    }

    [Fact]
    public void Find_ReturnsNull_BelowThreshold_ButMatchStillReportsScore()
    {
        var lib = new TemplateLibrary(_dir);
        lib.Save("白", TestFrames.Solid(10, 10, 255, 255, 255), new RectangleF(0, 0, 1, 1), 0.9, 1.0);
        var black = TestFrames.Solid(50, 50, 0, 0, 0);

        Assert.Null(lib.Find(black, "白", 1.0));
        Assert.Equal(0.0, lib.Match(black, "白", 1.0)!.Value.Score, 3);
    }

    [Fact]
    public void Find_ScalesTemplate_WhenDprDiffers()
    {
        var f = TestFrames.Pattern();
        var lib = new TemplateLibrary(_dir);
        lib.Save("白塊", f.Crop(new Rectangle(57, 31, 30, 20)), new RectangleF(0, 0, 1, 1), 0.9, 1.0);

        var m = lib.Find(f.Resize(2), "白塊", 2.0);

        Assert.NotNull(m);
        Assert.Equal(new Rectangle(114, 62, 60, 40), m.Value.Bounds);
    }

    [Fact]
    public void Find_SearchesOnlyInsideRegion()
    {
        var f = TestFrames.Pattern();
        var lib = new TemplateLibrary(_dir);
        // 只在右半邊找：白塊在左半邊，應該找不到
        lib.Save("白塊", f.Crop(new Rectangle(57, 31, 30, 20)), new RectangleF(0.5f, 0, 0.5f, 1), 0.99, 1.0);

        Assert.Null(lib.Find(f, "白塊", 1.0));
    }

    [Fact]
    public void Save_RejectsInvalidName()
    {
        var lib = new TemplateLibrary(_dir);

        Assert.Throws<ArgumentException>(() => lib.Save("a/b", TestFrames.Solid(2, 2, 0, 0, 0), new RectangleF(0, 0, 1, 1), 0.9, 1.0));
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter TemplateLibraryTests`
Expected: 編譯失敗，訊息含 `The type or namespace name 'TemplateLibrary' could not be found`

- [ ] **Step 3: 加 AppPaths.TemplatesDir**

在 `C:\Ro\RoHelper\AppPaths.cs` 的 `EngineCacheFile` 屬性後面加：
```csharp

    /// <summary>視覺判斷用的範本圖片（每個範本一個 PNG，加一個同名 JSON 記搜尋範圍、門檻、截圖時的 dpr）</summary>
    public static string TemplatesDir => Path.Combine(DataDir, "templates");
```

- [ ] **Step 4: 實作 TemplateLibrary**

`C:\Ro\RoHelper\Vision\TemplateLibrary.cs`：
```csharp
using System.Drawing.Imaging;
using System.Text.Json;

using RoHelper.Storage;

namespace RoHelper.Vision;

/// <summary>一個範本：圖片、搜尋範圍（佔截圖的比例 0～1）、門檻、截圖當下的 dpr</summary>
public sealed record TemplateInfo(string Name, Frame Image, RectangleF Region, double Threshold, double Dpr);

/// <summary>
/// 範本庫：資料夾裡每個範本是「名稱.png」＋「名稱.json」。JSON 壞掉或不存在時用預設值（整張截圖、門檻 0.9、dpr 1），不讓單一範本擋住其他範本。
/// 為什麼要記 dpr：遊戲的按鈕和文字是固定 CSS 大小，Windows 顯示縮放（100%／125%／150%）改了，截圖上的像素大小就跟著變；
/// 比對前依「目前 dpr ÷ 截圖當下的 dpr」縮放範本，換一台電腦或改了縮放，範本照樣能用。
/// 搜尋範圍用比例而不是像素：視窗大小會變，比例是比較穩定的近似值，實際值由計畫 2 起依各個按鈕校正。
/// </summary>
public sealed class TemplateLibrary
{
    public const double DefaultThreshold = 0.9;

    private readonly string _dir;
    private readonly Dictionary<string, TemplateInfo> _items = new(StringComparer.OrdinalIgnoreCase);

    public TemplateLibrary(string dir)
    {
        _dir = dir;
        Reload();
    }

    public IReadOnlyCollection<string> Names => _items.Keys;

    public TemplateInfo? Get(string name) => _items.GetValueOrDefault(name);

    public void Reload()
    {
        _items.Clear();
        if (!Directory.Exists(_dir))
            return;
        foreach (var png in Directory.EnumerateFiles(_dir, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(png);
            // 刻意先讀成 byte[] 再建 Bitmap：new Bitmap(path) 會鎖住檔案，之後就不能用同一個名稱覆蓋存檔
            using var ms = new MemoryStream(File.ReadAllBytes(png));
            using var bmp = new Bitmap(ms);
            var meta = ReadMeta(Path.ChangeExtension(png, ".json"));
            _items[name] = new TemplateInfo(name, Frame.FromBitmap(bmp), new RectangleF(meta.X, meta.Y, meta.W, meta.H), meta.Threshold, meta.Dpr);
        }
    }

    public void Save(string name, Frame image, RectangleF region, double threshold, double dpr)
    {
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("範本名稱不能是空的，也不能有 \\ / : * ? \" < > | 這些字元", nameof(name));
        Directory.CreateDirectory(_dir);
        using (var bmp = image.ToBitmap())
            bmp.Save(Path.Combine(_dir, name + ".png"), ImageFormat.Png);
        var meta = new TemplateMeta(region.X, region.Y, region.Width, region.Height, threshold, dpr);
        JsonFile.WriteText(Path.Combine(_dir, name + ".json"), JsonSerializer.Serialize(meta, JsonFile.Options));
        _items[name] = new TemplateInfo(name, image, region, threshold, dpr);
    }

    /// <summary>找最像的位置，不管分數有沒有過門檻（除錯視窗要顯示分數）。範本不存在或比搜尋範圍大時回 null</summary>
    public MatchResult? Match(Frame frame, string name, double dpr)
    {
        if (Get(name) is not { } t)
            return null;
        var image = Math.Abs(dpr - t.Dpr) < 0.01 ? t.Image : t.Image.Resize(dpr / t.Dpr);
        var region = new Rectangle(
            (int)(t.Region.X * frame.Width),
            (int)(t.Region.Y * frame.Height),
            (int)Math.Ceiling(t.Region.Width * frame.Width),
            (int)Math.Ceiling(t.Region.Height * frame.Height));
        return TemplateMatcher.Find(frame, image, region);
    }

    /// <summary>找範本，分數達到門檻才算找到</summary>
    public MatchResult? Find(Frame frame, string name, double dpr)
        => Match(frame, name, dpr) is { } m && m.Score >= Get(name)!.Threshold ? m : null;

    private static TemplateMeta ReadMeta(string path)
    {
        try
        {
            return JsonFile.Read(path)?.Deserialize<TemplateMeta>(JsonFile.Options) ?? TemplateMeta.Default;
        }
        catch (JsonException)
        {
            return TemplateMeta.Default;
        }
    }

    /// <summary>範本的 JSON 設定（X、Y、W、H 是佔截圖的比例）。用 internal 不用 private：保險起見讓 System.Text.Json 一定能用反射建構</summary>
    internal sealed record TemplateMeta(float X, float Y, float W, float H, double Threshold, double Dpr)
    {
        public static TemplateMeta Default { get; } = new(0, 0, 1, 1, DefaultThreshold, 1.0);
    }
}
```

- [ ] **Step 5: 執行測試，確認通過**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter TemplateLibraryTests`
Expected: `Passed!  - Failed: 0, Passed: 5`

- [ ] **Step 6: 執行全部測試**

Run: `cd /c/Ro && dotnet test RoHelper.Tests`
Expected: `Passed!  - Failed: 0, Passed: 18`

- [ ] **Step 7: 檢查點**（有 git：`git add -A && git commit -m "feat(vision): TemplateLibrary 範本庫"`）

---

### Task 5: OcrReader 繁中 OCR

**Files:**
- Create: `C:\Ro\RoHelper\Vision\OcrReader.cs`
- Test: `C:\Ro\RoHelper.Tests\OcrReaderTests.cs`

**Interfaces:**
- Consumes: `Frame`（Task 1）
- Produces:
  - `sealed record OcrLine(string Text, Rectangle Bounds)`：Text 已去掉中文字之間的空白；Bounds 是截圖座標
  - `static OcrReader? OcrReader.TryCreate(out string error)`：沒有繁中 OCR 時回 null，error 說明怎麼安裝
  - `Task<IReadOnlyList<OcrLine>> ReadAsync(Frame frame, Rectangle region, double upscale = 2)`
  - `static string OcrReader.Compact(string text)`

前置條件：這台電腦已確認有 `zh-Hant-TW` OCR（`OcrEngine.AvailableRecognizerLanguages` 查得到）。

- [ ] **Step 1: 寫會失敗的測試**

`C:\Ro\RoHelper.Tests\OcrReaderTests.cs`：
```csharp
using System.Drawing.Text;

using RoHelper.Vision;

namespace RoHelper.Tests;

public class OcrReaderTests
{
    [Theory]
    [InlineData("單 人 大 廳", "單人大廳")]
    [InlineData("返回 角色", "返回角色")]
    [InlineData("HP 120 / 300", "HP 120 / 300")]
    [InlineData("雷 爆 術 Deity + 3", "雷爆術 Deity + 3")]
    public void Compact_RemovesSpacesBetweenCjkOnly(string input, string expected)
    {
        Assert.Equal(expected, OcrReader.Compact(input));
    }

    [Fact]
    public async Task ReadAsync_ReadsRenderedTraditionalChinese()
    {
        var reader = OcrReader.TryCreate(out var error);
        Assert.True(reader is not null, error);

        var frame = Render("單人大廳", 360, 80);
        var lines = await reader!.ReadAsync(frame, new Rectangle(0, 0, frame.Width, frame.Height));

        Assert.Contains(lines, l => l.Text.Contains("單人大廳"));
        Assert.All(lines, l => Assert.True(new Rectangle(0, 0, frame.Width, frame.Height).Contains(l.Bounds)));
    }

    [Fact]
    public async Task ReadAsync_ReportsBoundsInFrameCoordinates()
    {
        var reader = OcrReader.TryCreate(out _)!;
        var text = Render("返回角色", 300, 70);
        var frame = TestFrames.Solid(800, 400, 255, 255, 255);
        Paste(text, frame, 400, 200);

        var lines = await reader.ReadAsync(frame, new Rectangle(350, 150, 400, 200));

        var line = Assert.Single(lines, l => l.Text.Contains("返回角色"));
        Assert.InRange(line.Bounds.X, 400, 470);
        Assert.InRange(line.Bounds.Y, 200, 250);
    }

    private static Frame Render(string text, int w, int h)
    {
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        using (var font = new Font("Microsoft JhengHei", 28, GraphicsUnit.Pixel))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.DrawString(text, font, Brushes.Black, 10, 15);
        }
        return Frame.FromBitmap(bmp);
    }

    private static void Paste(Frame src, Frame dst, int ox, int oy)
    {
        for (var y = 0; y < src.Height; y++)
            Buffer.BlockCopy(src.Pixels, y * src.Width * 4, dst.Pixels, ((oy + y) * dst.Width + ox) * 4, src.Width * 4);
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter OcrReaderTests`
Expected: 編譯失敗，訊息含 `The name 'OcrReader' does not exist`

- [ ] **Step 3: 實作**

`C:\Ro\RoHelper\Vision\OcrReader.cs`：
```csharp
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;

using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace RoHelper.Vision;

/// <summary>OCR 讀到的一行字（Bounds 是截圖座標）</summary>
public sealed record OcrLine(string Text, Rectangle Bounds);

/// <summary>
/// Windows 內建的繁體中文 OCR（Windows.Media.Ocr）。
/// 刻意不用 Tesseract：要另外帶 chi_tra 語言檔（數十 MB），而且繁中辨識率不如 Windows 內建的。
/// 建立失敗（沒裝繁中 OCR 語言套件）時 <see cref="TryCreate"/> 回 null、不拋例外：OCR 只是視覺功能的一部分，沒有它其他功能照用。
/// </summary>
public sealed partial class OcrReader
{
    public const string LanguageTag = "zh-Hant-TW";

    private readonly OcrEngine _engine;

    private OcrReader(OcrEngine engine) => _engine = engine;

    public static OcrReader? TryCreate(out string error)
    {
        var lang = new Language(LanguageTag);
        if (OcrEngine.IsLanguageSupported(lang) && OcrEngine.TryCreateFromLanguage(lang) is { } engine)
        {
            error = "";
            return new OcrReader(engine);
        }
        error = "這台電腦沒有繁體中文 OCR：Windows 設定 → 時間與語言 → 語言與地區 → 新增「中文（台灣）」，並勾選「光學字元辨識」。";
        return null;
    }

    /// <summary>
    /// 讀 region 裡的文字。
    /// upscale：先放大幾倍再辨識。遊戲字體小，實測放大 2 倍辨識率明顯較好；放大後超過 OCR 的尺寸上限會自動降低倍數。
    /// </summary>
    public async Task<IReadOnlyList<OcrLine>> ReadAsync(Frame frame, Rectangle region, double upscale = 2)
    {
        region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
        if (region.Width == 0 || region.Height == 0)
            return [];

        // 步驟 1：裁切並平滑放大（bicubic；Frame.Resize 是最近鄰，字的邊緣會鋸齒化）
        var crop = frame.Crop(region);
        var scale = Math.Min(upscale, (double)OcrEngine.MaxImageDimension / Math.Max(crop.Width, crop.Height));
        var image = Math.Abs(scale - 1) < 0.01 ? crop : SmoothResize(crop, scale);

        // 步驟 2：辨識
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            image.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        var result = await _engine.RecognizeAsync(bitmap);

        // 步驟 3：座標換回截圖座標（除以放大倍數、加上裁切起點）
        var actual = (double)image.Width / crop.Width;
        var lines = new List<OcrLine>();
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0)
                continue;
            var x0 = line.Words.Min(w => w.BoundingRect.X);
            var y0 = line.Words.Min(w => w.BoundingRect.Y);
            var x1 = line.Words.Max(w => w.BoundingRect.X + w.BoundingRect.Width);
            var y1 = line.Words.Max(w => w.BoundingRect.Y + w.BoundingRect.Height);
            var bounds = Rectangle.FromLTRB(
                region.X + (int)(x0 / actual), region.Y + (int)(y0 / actual),
                region.X + (int)Math.Ceiling(x1 / actual), region.Y + (int)Math.Ceiling(y1 / actual));
            lines.Add(new OcrLine(Compact(line.Text), Rectangle.Intersect(bounds, region)));
        }
        return lines;
    }

    /// <summary>
    /// Windows OCR 辨識中文時，每個字之間會插空白（「單 人 大 廳」）。只拿掉「中文字跟中文字之間」的空白，英數之間的保留（「HP 120 / 300」）。
    /// </summary>
    public static string Compact(string text) => CjkGap().Replace(text, "");

    // 中日韓文字、全形符號
    [GeneratedRegex(@"(?<=[\u3000-\u9fff\uff00-\uffef])\s+(?=[\u3000-\u9fff\uff00-\uffef])")]
    private static partial Regex CjkGap();

    private static Frame SmoothResize(Frame src, double scale)
    {
        var w = Math.Max(1, (int)Math.Round(src.Width * scale));
        var h = Math.Max(1, (int)Math.Round(src.Height * scale));
        using var from = src.ToBitmap();
        using var to = new Bitmap(w, h);
        using (var g = Graphics.FromImage(to))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(from, 0, 0, w, h);
        }
        return Frame.FromBitmap(to);
    }
}
```

注意 `Compact` 的測試案例 `"雷 爆 術 Deity + 3"`：「術」跟「D」之間的空白不是中文字夾中文字，會保留，所以結果是 `"雷爆術 Deity + 3"`。

- [ ] **Step 4: 執行測試，確認通過**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter OcrReaderTests`
Expected: `Passed!  - Failed: 0, Passed: 6`

如果 `ReadAsync_ReadsRenderedTraditionalChinese` 失敗，先看 assertion 訊息：顯示的是 `TryCreate` 的 error，表示 OCR 語言沒裝；如果是 `Contains` 失敗，把 `lines` 印出來看讀成了什麼字，再調整測試的字型大小（不要降低斷言標準）。

- [ ] **Step 5: 檢查點**（有 git：`git add -A && git commit -m "feat(vision): OcrReader 繁中 OCR"`）

---

### Task 6: 截圖、CDP 輸入、VisionKit

**Files:**
- Create: `C:\Ro\RoHelper\Vision\ScreenGrabber.cs`
- Create: `C:\Ro\RoHelper\Vision\CdpInput.cs`
- Create: `C:\Ro\RoHelper\Vision\VisionKit.cs`

**Interfaces:**
- Consumes: `Frame`、`ViewportMapper`、`TemplateLibrary`、`OcrReader`（Task 1～5）、`AppPaths.TemplatesDir`（Task 4）
- Produces:
  - `ScreenGrabber(CoreWebView2 core)`；`Task<Frame> CaptureAsync()`
  - `CdpInput(CoreWebView2 core)`；`Task MoveAsync(double x, double y)`、`Task ClickAsync(double x, double y)`、`Task WheelAsync(double x, double y, double deltaY)`、`Task KeyAsync(char key)`（座標都是 CSS 像素）
  - `VisionKit(CoreWebView2 core, Func<double> dpr)`；屬性 `ScreenGrabber Grabber`、`CdpInput Input`、`TemplateLibrary Templates`、`OcrReader? Ocr`、`string OcrError`、`ViewportMapper Viewport`

這三個類別直接包 WebView2，沒辦法寫單元測試；Task 7 用除錯視窗在真實遊戲上驗證。

- [ ] **Step 1: 實作 ScreenGrabber**

`C:\Ro\RoHelper\Vision\ScreenGrabber.cs`：
```csharp
using Microsoft.Web.WebView2.Core;

namespace RoHelper.Vision;

/// <summary>
/// 截下 WebView2 目前顯示的畫面。用 CapturePreviewAsync：由瀏覽器把畫面輸出成圖片，頁面完全不知道被截圖，也不用執行任何 JS。
/// 刻意不用螢幕截圖（Graphics.CopyFromScreen）：視窗被擋住或移到螢幕外（掛機）時，截到的是擋在前面的東西。
/// 必須在 UI 執行緒呼叫（WebView2 的限制）。
/// 刻意用 PNG 不用 JPEG：JPEG 會讓顏色偏掉幾個數值，色塊判斷的門檻會不準。
/// </summary>
public sealed class ScreenGrabber
{
    private readonly CoreWebView2 _core;

    public ScreenGrabber(CoreWebView2 core) => _core = core;

    public async Task<Frame> CaptureAsync()
    {
        using var ms = new MemoryStream();
        await _core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
        ms.Position = 0;
        using var bmp = new Bitmap(ms);
        return Frame.FromBitmap(bmp);
    }
}
```

- [ ] **Step 2: 實作 CdpInput**

`C:\Ro\RoHelper\Vision\CdpInput.cs`：
```csharp
using System.Text.Json.Nodes;

using Microsoft.Web.WebView2.Core;

namespace RoHelper.Vision;

/// <summary>
/// 透過 DevTools Protocol 的 Input 網域送出滑鼠、滾輪、鍵盤輸入（座標是 CSS 像素）。
/// 這是瀏覽器層級的輸入，跟實體滑鼠走同一條輸入管線：網頁收到的事件 isTrusted = true，而且不會在頁面執行任何 JS。
/// 刻意不用 Windows 的 SendInput：那要搶前景視窗、真的移動游標，掛機時會干擾使用者正在做的事；視窗移到螢幕外時也點不到。
/// </summary>
public sealed class CdpInput
{
    private readonly CoreWebView2 _core;

    public CdpInput(CoreWebView2 core) => _core = core;

    public Task MoveAsync(double x, double y) => MouseAsync("mouseMoved", x, y, "none", 0, 0);

    /// <summary>移過去、按下、放開。先送 mouseMoved：有些遊戲 UI 要先收到 hover 才接受點擊</summary>
    public async Task ClickAsync(double x, double y)
    {
        await MouseAsync("mouseMoved", x, y, "none", 0, 0);
        await MouseAsync("mousePressed", x, y, "left", 1, 1);
        await MouseAsync("mouseReleased", x, y, "left", 0, 1);
    }

    /// <summary>滾輪。deltaY 正數＝往下滾（遊戲裡是把鏡頭拉遠）</summary>
    public Task WheelAsync(double x, double y, double deltaY)
        => SendAsync("Input.dispatchMouseEvent", new JsonObject
        {
            ["type"] = "mouseWheel",
            ["x"] = x,
            ["y"] = y,
            ["deltaX"] = 0,
            ["deltaY"] = deltaY,
        });

    /// <summary>
    /// 按一個鍵（數字 0～9 或英文字母）。keyDown 要帶 text，網頁才會收到 keypress／input，跟實體鍵盤一樣。
    /// </summary>
    public async Task KeyAsync(char key)
    {
        var upper = char.ToUpperInvariant(key);
        var code = char.IsDigit(key) ? $"Digit{key}" : $"Key{upper}";
        var text = key.ToString();
        await SendAsync("Input.dispatchKeyEvent", new JsonObject
        {
            ["type"] = "keyDown",
            ["key"] = text,
            ["code"] = code,
            ["text"] = text,
            ["windowsVirtualKeyCode"] = (int)upper,
            ["nativeVirtualKeyCode"] = (int)upper,
        });
        await SendAsync("Input.dispatchKeyEvent", new JsonObject
        {
            ["type"] = "keyUp",
            ["key"] = text,
            ["code"] = code,
            ["windowsVirtualKeyCode"] = (int)upper,
            ["nativeVirtualKeyCode"] = (int)upper,
        });
    }

    private Task MouseAsync(string type, double x, double y, string button, int buttons, int clickCount)
        => SendAsync("Input.dispatchMouseEvent", new JsonObject
        {
            ["type"] = type,
            ["x"] = x,
            ["y"] = y,
            ["button"] = button,
            ["buttons"] = buttons,
            ["clickCount"] = clickCount,
        });

    private Task SendAsync(string method, JsonObject args)
        => _core.CallDevToolsProtocolMethodAsync(method, args.ToJsonString());
}
```

- [ ] **Step 3: 實作 VisionKit**

`C:\Ro\RoHelper\Vision\VisionKit.cs`：
```csharp
using Microsoft.Web.WebView2.Core;

namespace RoHelper.Vision;

/// <summary>
/// 視覺自動化的工具組：截圖、輸入、範本、OCR、座標換算。除錯視窗用它；計畫 2 起的視覺引擎也用同一組。
/// dpr 用委派即時讀：使用者把視窗拖到另一個縮放比例不同的螢幕時，dpr 會跟著變。
/// </summary>
public sealed class VisionKit
{
    private readonly Func<double> _dpr;

    public VisionKit(CoreWebView2 core, Func<double> dpr)
    {
        _dpr = dpr;
        Grabber = new ScreenGrabber(core);
        Input = new CdpInput(core);
        Templates = new TemplateLibrary(AppPaths.TemplatesDir);
        Ocr = OcrReader.TryCreate(out var error);
        OcrError = error;
    }

    public ScreenGrabber Grabber { get; }
    public CdpInput Input { get; }
    public TemplateLibrary Templates { get; }

    /// <summary>沒有繁中 OCR 時是 null，原因看 <see cref="OcrError"/></summary>
    public OcrReader? Ocr { get; }

    public string OcrError { get; }

    public ViewportMapper Viewport => new(_dpr());
}
```

- [ ] **Step 4: 建置**

Run: `cd /c/Ro && dotnet build RoHelper/RoHelper.csproj`
Expected: `Build succeeded.`，0 個 error

- [ ] **Step 5: 檢查點**（有 git：`git add -A && git commit -m "feat(vision): 截圖、CDP 輸入、VisionKit"`）

---

### Task 7: 視覺除錯視窗＋實機驗證

**Files:**
- Create: `C:\Ro\RoHelper\Forms\ZoomMapper.cs`
- Test: `C:\Ro\RoHelper.Tests\ZoomMapperTests.cs`
- Create: `C:\Ro\RoHelper\Forms\VisionDebugForm.cs`
- Modify: `C:\Ro\RoHelper\Forms\GameForm.cs`（欄位、選單、開視窗的方法）

**Interfaces:**
- Consumes: `VisionKit`（Task 6）及其所有成員、`TemplateLibrary.DefaultThreshold`、`Match`、`Save`、`Reload`、`Names`（Task 4）、`OcrReader.ReadAsync`（Task 5）
- Produces:
  - `static Point? ZoomMapper.ToImage(Size box, Size image, Point p)`、`static Rectangle ZoomMapper.ToBox(Size box, Size image, Rectangle r)`
  - `VisionDebugForm(VisionKit kit)`
  - 遊戲視窗選單「視覺除錯」

- [ ] **Step 1: 寫 ZoomMapper 的失敗測試**

`C:\Ro\RoHelper.Tests\ZoomMapperTests.cs`：
```csharp
using RoHelper.Forms;

namespace RoHelper.Tests;

public class ZoomMapperTests
{
    // 400×100 的圖放進 200×100 的框：縮成 0.5 倍（200×50），上下各留 25
    private static readonly Size Box = new(200, 100);
    private static readonly Size Image = new(400, 100);

    [Fact]
    public void ToImage_MapsThroughScaleAndOffset()
    {
        Assert.Equal(new Point(200, 50), ZoomMapper.ToImage(Box, Image, new Point(100, 50)));
    }

    [Fact]
    public void ToImage_ReturnsNull_OutsideImage()
    {
        Assert.Null(ZoomMapper.ToImage(Box, Image, new Point(100, 10)));
    }

    [Fact]
    public void ToBox_IsInverseOfToImage()
    {
        Assert.Equal(new Rectangle(50, 35, 50, 10), ZoomMapper.ToBox(Box, Image, new Rectangle(100, 20, 100, 20)));
    }
}
```

- [ ] **Step 2: 執行測試，確認失敗**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter ZoomMapperTests`
Expected: 編譯失敗，訊息含 `The name 'ZoomMapper' does not exist`

- [ ] **Step 3: 實作 ZoomMapper**

`C:\Ro\RoHelper\Forms\ZoomMapper.cs`：
```csharp
namespace RoHelper.Forms;

/// <summary>
/// PictureBox 的 SizeMode = Zoom（圖片等比縮放、置中）下，控制項座標 ↔ 圖片座標的換算。WinForms 沒有內建這個換算。
/// </summary>
public static class ZoomMapper
{
    /// <summary>控制項座標 → 圖片座標；點在圖片外面（上下或左右留白處）回 null</summary>
    public static Point? ToImage(Size box, Size image, Point p)
    {
        if (image.Width == 0 || image.Height == 0)
            return null;
        var (s, ox, oy) = Fit(box, image);
        var x = (p.X - ox) / s;
        var y = (p.Y - oy) / s;
        if (x < 0 || y < 0 || x >= image.Width || y >= image.Height)
            return null;
        return new Point((int)x, (int)y);
    }

    /// <summary>圖片座標的矩形 → 控制項座標（畫框選、比對結果用）</summary>
    public static Rectangle ToBox(Size box, Size image, Rectangle r)
    {
        var (s, ox, oy) = Fit(box, image);
        return new Rectangle((int)(ox + r.X * s), (int)(oy + r.Y * s), (int)(r.Width * s), (int)(r.Height * s));
    }

    private static (double Scale, double OffsetX, double OffsetY) Fit(Size box, Size image)
    {
        var s = Math.Min((double)box.Width / image.Width, (double)box.Height / image.Height);
        return (s, (box.Width - image.Width * s) / 2, (box.Height - image.Height * s) / 2);
    }
}
```

- [ ] **Step 4: 執行測試，確認通過**

Run: `cd /c/Ro && dotnet test RoHelper.Tests --filter ZoomMapperTests`
Expected: `Passed!  - Failed: 0, Passed: 3`

- [ ] **Step 5: 實作 VisionDebugForm**

`C:\Ro\RoHelper\Forms\VisionDebugForm.cs`：
```csharp
using System.Diagnostics;

using RoHelper.Vision;

namespace RoHelper.Forms;

/// <summary>
/// 視覺除錯視窗：即時顯示 exe 截到的遊戲畫面，用來校正視覺判斷、收集範本。
/// 功能：
/// - 點擊測試：點預覽圖 → 用 CDP 在遊戲的同一點送真實點擊，確認座標換算（dpr）正確
/// - 框選後 OCR、框選後存成範本、對目前畫面找範本（顯示分數和耗時）
/// 全程只截圖和送輸入，不在遊戲頁面執行任何 JS。
/// 按 X 只是隱藏（跟面板一樣），隱藏時截圖計時器也停掉，不在背景白白截圖。
/// </summary>
public sealed class VisionDebugForm : Form
{
    private readonly VisionKit _kit;
    private readonly PictureBox _view = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
    private readonly TextBox _log = new() { Dock = DockStyle.Bottom, Height = 140, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly CheckBox _live = new() { Text = "即時更新", AutoSize = true, Checked = true };
    private readonly RadioButton _clickMode = new() { Text = "點擊測試", AutoSize = true, Checked = true };
    private readonly RadioButton _selectMode = new() { Text = "框選", AutoSize = true };
    private readonly TextBox _templateName = new() { Width = 120, PlaceholderText = "範本名稱" };
    private readonly ComboBox _templates = new() { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };

    private Frame? _frame;
    private Point? _dragStart;

    /// <summary>框選範圍（圖片座標）</summary>
    private Rectangle? _selection;

    /// <summary>最近一次找範本的結果（圖片座標）</summary>
    private Rectangle? _match;

    /// <summary>截圖進行中：CapturePreviewAsync 偶爾會超過計時器間隔，不重疊呼叫</summary>
    private bool _capturing;

    public VisionDebugForm(VisionKit kit)
    {
        _kit = kit;
        Text = "視覺除錯";
        Font = new Font("Microsoft JhengHei UI", 9f);
        Size = new Size(LogicalToDeviceUnits(1000), LogicalToDeviceUnits(720));

        var snap = new Button { Text = "截一張", AutoSize = true };
        var ocr = new Button { Text = "OCR 框選區", AutoSize = true };
        var save = new Button { Text = "存成範本", AutoSize = true };
        var find = new Button { Text = "找範本", AutoSize = true };
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true };
        bar.Controls.AddRange([_live, snap, _clickMode, _selectMode, ocr, _templateName, save, _templates, find]);

        // 先加 Fill 再加 Top／Bottom：WinForms 依 z-order 反向處理 Dock
        Controls.Add(_view);
        Controls.Add(_log);
        Controls.Add(bar);

        _timer.Tick += async (_, _) =>
        {
            if (_live.Checked)
                await CaptureAsync();
        };
        snap.Click += async (_, _) => await CaptureAsync();
        ocr.Click += async (_, _) => await OcrSelectionAsync();
        save.Click += (_, _) => SaveTemplate();
        find.Click += (_, _) => FindTemplate();
        _view.MouseDown += OnViewMouseDown;
        _view.MouseMove += OnViewMouseMove;
        _view.MouseUp += OnViewMouseUp;
        _view.Paint += OnViewPaint;

        if (_kit.Ocr is null)
            Log(_kit.OcrError);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        _timer.Enabled = Visible;
        if (Visible)
            RefreshTemplates();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _view.Image?.Dispose();
        }
        base.Dispose(disposing);
    }

    #region 截圖

    private async Task CaptureAsync()
    {
        if (_capturing)
            return;
        _capturing = true;
        try
        {
            var sw = Stopwatch.StartNew();
            var frame = await _kit.Grabber.CaptureAsync();
            var ms = sw.ElapsedMilliseconds;
            _frame = frame;
            var old = _view.Image;
            _view.Image = frame.ToBitmap();
            old?.Dispose();
            Text = $"視覺除錯 — {frame.Width}×{frame.Height}，截圖 {ms} ms，dpr {_kit.Viewport.Dpr:0.##}";
        }
        catch (Exception ex)
        {
            // 除錯工具：截圖失敗（頁面重新載入中、WebView2 忙碌）只記下來，下一輪再試
            _live.Checked = false;
            Log($"截圖失敗，已停止即時更新 : {ex.Message}");
        }
        finally
        {
            _capturing = false;
        }
    }

    #endregion

    #region 滑鼠：點擊測試／框選

    private void OnViewMouseDown(object? sender, MouseEventArgs e)
    {
        if (!_selectMode.Checked || ToImage(e.Location) is not { } p)
            return;
        _dragStart = p;
        _selection = null;
        _match = null;
        _view.Invalidate();
    }

    private void OnViewMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragStart is not { } a || ToImage(e.Location) is not { } b)
            return;
        _selection = Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X) + 1, Math.Max(a.Y, b.Y) + 1);
        _view.Invalidate();
    }

    private async void OnViewMouseUp(object? sender, MouseEventArgs e)
    {
        if (_selectMode.Checked)
        {
            _dragStart = null;
            if (_selection is { } r)
                Log($"框選：({r.X}, {r.Y}) {r.Width}×{r.Height}");
            return;
        }
        if (e.Button != MouseButtons.Left || ToImage(e.Location) is not { } p)
            return;

        var (x, y) = _kit.Viewport.ToCss(p.X, p.Y);
        try
        {
            await _kit.Input.ClickAsync(x, y);
            Log($"點擊：截圖 ({p.X}, {p.Y}) → 網頁 ({x:0}, {y:0})");
        }
        catch (Exception ex)
        {
            Log($"點擊失敗 : {ex.Message}");
        }
    }

    private void OnViewPaint(object? sender, PaintEventArgs e)
    {
        if (_frame is null)
            return;
        var size = new Size(_frame.Width, _frame.Height);
        if (_selection is { } sel)
        {
            using var pen = new Pen(Color.Lime, 2);
            e.Graphics.DrawRectangle(pen, ZoomMapper.ToBox(_view.ClientSize, size, sel));
        }
        if (_match is { } m)
        {
            using var pen = new Pen(Color.Magenta, 2);
            e.Graphics.DrawRectangle(pen, ZoomMapper.ToBox(_view.ClientSize, size, m));
        }
    }

    private Point? ToImage(Point p)
        => _frame is null ? null : ZoomMapper.ToImage(_view.ClientSize, new Size(_frame.Width, _frame.Height), p);

    #endregion

    #region OCR／範本

    private async Task OcrSelectionAsync()
    {
        if (_kit.Ocr is null)
        {
            Log(_kit.OcrError);
            return;
        }
        if (_frame is null || _selection is not { } r)
        {
            Log("先切到「框選」，在畫面上拖出一塊範圍");
            return;
        }
        var sw = Stopwatch.StartNew();
        var lines = await _kit.Ocr.ReadAsync(_frame, r);
        Log($"OCR（{sw.ElapsedMilliseconds} ms）：" + (lines.Count == 0 ? "（沒有讀到字）" : string.Join(" ｜ ", lines.Select(l => l.Text))));
    }

    /// <summary>
    /// 存成範本。搜尋範圍先存整張截圖、門檻用預設值；要縮小範圍或調門檻，直接改範本旁邊的 JSON（計畫 2 起依各個按鈕校正）。
    /// </summary>
    private void SaveTemplate()
    {
        var name = _templateName.Text.Trim();
        if (_frame is null || _selection is not { } r || name.Length == 0)
        {
            Log("要先框選範圍，並填範本名稱");
            return;
        }
        try
        {
            _kit.Templates.Save(name, _frame.Crop(r), new RectangleF(0, 0, 1, 1), TemplateLibrary.DefaultThreshold, _kit.Viewport.Dpr);
        }
        catch (ArgumentException ex)
        {
            Log(ex.Message);
            return;
        }
        Log($"已存範本「{name}」（{r.Width}×{r.Height}）");
        RefreshTemplates();
        _templates.SelectedItem = name;
    }

    private void FindTemplate()
    {
        if (_frame is null || _templates.SelectedItem is not string name)
        {
            Log("要先有截圖，並選一個範本");
            return;
        }
        var sw = Stopwatch.StartNew();
        var m = _kit.Templates.Match(_frame, name, _kit.Viewport.Dpr);
        var ms = sw.ElapsedMilliseconds;
        var threshold = _kit.Templates.Get(name)!.Threshold;
        _match = m?.Bounds;
        _view.Invalidate();
        Log(m is { } r
            ? $"找「{name}」（{ms} ms）：({r.Bounds.X}, {r.Bounds.Y}) 分數 {r.Score:0.000}，門檻 {threshold:0.00} → {(r.Score >= threshold ? "找到" : "沒找到")}"
            : $"找「{name}」：範本比搜尋範圍還大");
    }

    private void RefreshTemplates()
    {
        var selected = _templates.SelectedItem as string;
        _kit.Templates.Reload();
        _templates.Items.Clear();
        _templates.Items.AddRange([.. _kit.Templates.Names.Order()]);
        if (selected is not null && _templates.Items.Contains(selected))
            _templates.SelectedItem = selected;
    }

    #endregion

    private void Log(string message) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
}
```

- [ ] **Step 6: GameForm 加選單**

`C:\Ro\RoHelper\Forms\GameForm.cs`：

(a) using 區塊的專案分組加一行（放在 `using RoHelper.Storage;` 後面）：
```csharp
using RoHelper.Vision;
```

(b) 在 `private string? _scriptId;` 後面加欄位：
```csharp

    /// <summary>視覺工具組。第一次開「視覺除錯」時才建立（要等 WebView2 初始化完成）</summary>
    private VisionKit? _vision;

    private VisionDebugForm? _visionDebug;
```

(c) 建構子裡，在 `menu.Items.Add(new ToolStripMenuItem("檢查更新", ...));` 後面加：
```csharp
        menu.Items.Add(new ToolStripMenuItem("視覺除錯", null, (_, _) => OpenVisionDebug()));
```

(d) 在 `TogglePanel()` 方法前面加：
```csharp
    /// <summary>
    /// 開視覺除錯視窗。dpr＝控制項的 DPI 縮放 × 頁面縮放：WebView2 依控制項的 DPI 決定實體像素，
    /// 刻意不從頁面讀 window.devicePixelRatio，那要在頁面執行 JS。
    /// </summary>
    private void OpenVisionDebug()
    {
        if (_webView.CoreWebView2 is not { } core)
            return;
        _vision ??= new VisionKit(core, () => _webView.DeviceDpi / 96.0 * _webView.ZoomFactor);
        if (_visionDebug is null || _visionDebug.IsDisposed)
            _visionDebug = new VisionDebugForm(_vision);
        _visionDebug.Show();
        _visionDebug.Activate();
    }
```

- [ ] **Step 7: 建置＋全部測試**

先關掉正在執行的 RoHelper.exe。
Run: `cd /c/Ro && dotnet build RoHelper/RoHelper.csproj && dotnet test RoHelper.Tests`
Expected: `Build succeeded.`，以及 `Passed!  - Failed: 0, Passed: 27`

- [ ] **Step 8: 實機驗證（手動，需要使用者在場）**

用 `dotnet run --project /c/Ro/RoHelper` 開程式、登入遊戲，然後按選單的「視覺除錯」，依序確認下列項目並記下結果：

1. **截圖**：預覽圖跟遊戲畫面一致；標題列的截圖耗時應該在 150 ms 以下（超過的話記下數字，計畫 2 要據此決定主迴圈間隔）。
2. **dpr 正確**：在遊戲視窗按 F12 開 DevTools，在 Console 輸入 `innerWidth`（這只是人工檢查，不屬於自動化），確認「截圖寬度 ÷ 除錯視窗標題的 dpr」≈ `innerWidth`，誤差在 ±2 以內。
3. **點擊**：選「點擊測試」，在預覽圖上點城裡的地面，角色應該走到該處；點一個 UI 按鈕（例如右上角的選單），應該有反應。
4. **掛機狀態**：按遊戲視窗的最小化（會移到螢幕外），除錯視窗仍然持續更新截圖，點擊測試仍然有效。
5. **OCR**：選「框選」，框住任一段中文 UI 文字，按「OCR 框選區」，記錄讀到的文字和耗時。
6. **範本**：框住一個按鈕、填名稱、按「存成範本」；切換到其他畫面再切回來，按「找範本」，分數應該 ≥ 0.9，洋紅色框對在按鈕上。

任何一項失敗：**先不要繼續**，把現象記下來回報（尤其 2、3、4 項失敗代表架構假設不成立，要先調整方案）。

- [ ] **Step 9: 檢查點**（有 git：`git add -A && git commit -m "feat(vision): 視覺除錯視窗"`）

---

### Task 8: README 更新

**Files:**
- Modify: `C:\Ro\RoHelper\README.md`

- [ ] **Step 1: 在「## 開發」區塊最後加上**

```markdown
- 測試：`dotnet test ..\RoHelper.Tests`（測試專案跟 RoHelper 資料夾平行，避免被主專案一起編譯）
- 建置輸出路徑：TFM 改成 `net8.0-windows10.0.19041.0`（OCR 需要 WinRT），所以輸出在 `bin\Debug\net8.0-windows10.0.19041.0\`

## 視覺判斷（開發中，不碰網頁）
目標：完全不在遊戲頁面注入 JS，只看截圖判斷、用瀏覽器層級的真實輸入操作。目前完成第 1 階段（框架＋除錯工具），自動化仍由 JS 引擎負責。
- 遊戲視窗選單「視覺除錯」：即時截圖、點擊測試、框選 OCR、存範本、找範本
- 範本存在 `%AppData%\RoHelper\templates`：每個範本是 `名稱.png`＋`名稱.json`（`X/Y/W/H`＝搜尋範圍佔截圖的比例、`Threshold`＝門檻、`Dpr`＝截圖當下的顯示縮放）
- OCR 需要 Windows 的繁體中文 OCR 語言套件（設定 → 時間與語言 → 語言與地區 → 中文（台灣）→ 光學字元辨識）
- 計畫書：`C:\Ro\docs\superpowers\plans\`
```

- [ ] **Step 2: 檢查點**（有 git：`git commit -am "docs: README 加視覺判斷說明"`）

---

## 實作時的修正（2026-11-20）

實作時發現計畫本身的三個疏漏，已在程式碼中修正（測試的斷言都沒改）：
1. 測試專案 csproj 少了 `<Using Include="Xunit" />`，`[Fact]` 編譯不過 → 已補上。
2. `TestFrames.Fill` 沒有裁到圖片範圍內，`Pattern(30, 20)` 畫方塊時陣列越界 → `Fill` 先 `Rectangle.Intersect`。
3. `TestFrames.Pattern` 原本的線性漸層 `(2x+3y) % 256` 沿 (45, −30) 平移後完全一樣，而且折回處有 255 的落差，粗找被假的重複位置騙走 → 改成放射狀漸層 `dx² + 4dy²`。

## 實機驗證結果（2026-11-20，遊戲畫面 1904 寬、dpr 1）

| 項目 | 結果 |
|---|---|
| 截圖 | `CapturePreviewAsync` 實測 1500～2200 ms，太慢 → **改用 PrintWindow（PW_RENDERFULLCONTENT），約 50 ms**。也比較過 CDP `Page.captureScreenshot`，比 PrintWindow 慢。`ScreenGrabber` 只保留 PrintWindow，建構子改收 WebView2 控制項的 hwnd |
| dpr | 截圖寬 ÷ dpr = 1904 = `innerWidth`，通過 |
| 點擊 | CDP 點擊角色會走、按鈕有反應，通過 |
| 掛機（移到螢幕外） | 截圖持續更新、點擊有效，通過 |
| OCR | 框「開始遊戲」按鈕讀到「開始遊戲」，通過 |
| 範本 | 「開始遊戲」分數 1.000、位置正確，通過；但全畫面搜尋要 723 ms → 計畫 2 為每個範本設定搜尋範圍 |

## Self-Review 紀錄

- **涵蓋範圍**：路線表的第 1 階段（截圖、範本比對、OCR、真實輸入、除錯視窗）分別對應 Task 6、3～4、5、6、7；「不碰網頁」寫進 Global Constraints，也由 Task 7 Step 8 的第 2～4 項實機驗證。
- **型別一致性**：`MatchResult`（Task 3）在 Task 4、7 使用；`TemplateLibrary.Match/Find/Save/Reload/Names/Get/DefaultThreshold`（Task 4）在 Task 7 使用；`VisionKit` 的屬性（Task 6）在 Task 7 使用；`ViewportMapper.ToCss`（Task 1）在 Task 7 使用。
- **測試數量**：Task 1：6、Task 2：3、Task 3：4、Task 4：5、Task 5：6（Theory 4＋Fact 2）、Task 7：3，合計 27。
