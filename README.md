# Antigravity Auto Pilot ⚡

**Antigravity Auto Pilot** là ứng dụng Windows Desktop (.NET 8 WPF) được thiết kế chuyên biệt để tự động phát hiện và kích hoạt các nút xác nhận trong **Google Antigravity IDE** (`Accept`, `Accept All`, `Continue`, `Run`, `Submit`, `Retry`, `Allow`, `Apply`, `Confirm`).

Ứng dụng giúp bạn chạy các phiên làm việc dài với Antigravity Agent một cách hoàn toàn tự động mà không cần phải canh chừng và bấm nút xác nhận thủ công.

---

## Tính năng nổi bật

- **Việt hóa toàn diện 100%**: Giao diện WPF hiện đại, bảng điều khiển, khay hệ thống, nhật ký và cảnh báo an toàn đều được bản địa hóa sang tiếng Việt tự nhiên, chuyên nghiệp.
- **Nhận diện bằng UI Automation**: Không sử dụng tọa độ chuột cố định (hard-coded coordinates). Tự động thích ứng khi cửa sổ Antigravity thay đổi kích thước, di chuyển, phóng to, thu nhỏ, hoặc đặt ở màn hình phụ (multi-monitor).
- **Không chiếm chuột & Không giật màn hình (Zero Jitter)**: Kích hoạt hoàn toàn trong nền bằng `InvokePattern` và `SelectionItemPattern`. Chuột và focus của bạn hoàn toàn đứng yên, thoải mái làm việc ở cửa sổ khác.
- **Bảo vệ dòng lệnh Terminal (Destructive Protection)**: Quét ngữ cảnh xung quanh nút bấm. Tự động **CHẶN NGAY** nếu phát hiện các câu lệnh nguy hiểm như `rm -rf`, `del /s`, `format`, `git reset --hard`, `DROP TABLE`, `docker system prune`, v.v.
- **Blacklist tuyệt đối**: Không bao giờ tự click các nút có tính chất hủy hoại (`Reject`, `Decline`, `Delete`, `Cancel`, `Discard`, `Reset`, `Terminate`, `Drop`, `Revert`, `Undo All`).
- **Tự động trả lời câu hỏi tương tác (`ask_question`)**: Tự động chọn Option 1 (`Yes` / `Allow` / `Recommended`) và gửi `Submit` ngay lập tức, kèm chống spam phím Enter.
- **Hệ thống thông báo âm thanh tập trung trong thư mục `Sounds/` (Bật/Tắt linh hoạt)**:
  - 🔔 `Sounds/done.mp3`: Thông báo khi Antigravity hoàn thành trọn vẹn chuỗi nhiệm vụ. Tự động nhận diện và loại trừ các phản hồi trung gian của agent khi đang thực hiện tác vụ dài (chứa từ khóa "Đang tải...", "Đang cài đặt...", "Downloading...", "In progress..."), ngăn chặn tuyệt đối tình trạng báo xong sớm khi công việc vẫn đang chạy dở dang.
  - ✨ `Sounds/accept_all.mp3`: Thông báo khi vừa tự động duyệt `Accept All` hoặc `Accept` diff.
  - ⚡ `Sounds/submit.mp3`: Thông báo khi vừa tự động duyệt gửi form câu hỏi tương tác.
  - 📋 `Sounds/plan.mp3`: Thông báo khi Antigravity đưa ra bản kế hoạch `Implementation Plan` cần xem xét.
  - Toàn bộ file âm thanh được gom gọn gàng vào thư mục `Sounds/`. Hỗ trợ tùy chỉnh âm lượng và bật/tắt độc lập từng loại âm thanh ngay trên giao diện hoặc lưu cấu hình vào `settings.json`.
- **Tự động đóng tab Implementation Plan sau khi Proceed**: Tự động nhận diện và đóng tab editor `implementation_plan.md` trong Antigravity IDE ngay sau khi kế hoạch được bấm Proceed (hoàn toàn KHÔNG xóa file markdown trên ổ cứng, chỉ đóng tab trong trình soạn thảo giúp màn hình làm việc luôn gọn gàng). Có thể bật/tắt tùy chọn này trên giao diện hoặc cấu hình `AutoCloseProceededPlans: true`.
- **Emergency Stop toàn cầu**: Nhấn `Ctrl + Shift + F12` dừng ngay lập tức mọi hành vi auto click, kể cả khi bạn đang thao tác ở cửa sổ khác. Khôi phục nhanh bằng `Ctrl + Shift + F11`.
- **Chế độ hoạt động linh hoạt**: 3 mode: **An Toàn (Safe)**, **Tiêu Chuẩn (Normal)**, **Tự Động Toàn Diện (Full Auto)**.
- **Chạy nền trên System Tray**: Thu gọn xuống khay hệ thống, hiển thị thông báo balloon khi có câu lệnh nguy hiểm bị chặn.
- **Hỗ trợ chạy đồng thời nhiều cửa sổ (Multi-Window Support)**: Tự động phát hiện và phê duyệt/submit song song trên tất cả các cửa sổ Antigravity IDE đang mở trên máy (không giới hạn số lượng workspace).
- **Tự động kết nối lại**: Theo dõi tiến trình Antigravity IDE và tự động phục hồi kết nối nếu Antigravity khởi động lại.

---

## 1. Yêu cầu hệ thống (Requirements)

- **Hệ điều hành**: Windows 10 / Windows 11 (64-bit).
- **Runtime / SDK**: .NET 8.0 Runtime hoặc .NET 8.0 SDK (nếu chạy bản portable self-contained thì không cần cài trước .NET runtime).
- **Target IDE**: Google Antigravity IDE (Desktop Client).

---

## 2. Hướng dẫn Build

Mở terminal (PowerShell hoặc Command Prompt) tại thư mục `tools/antigravity_auto_pilot`:

```bash
cd "tools/antigravity_auto_pilot"
```

### Restore và Build Debug

```bash
dotnet restore
dotnet build
```

### Build bản Release x64 Độc lập (Self-contained Single EXE)

```bash
dotnet publish -c Release -r win-x64 --self-contained true
```

File thực thi `.exe` sẽ được tạo tại:
`tools/antigravity_auto_pilot/bin/Release/net8.0-windows/win-x64/publish/AntigravityAutoPilot.exe`

---

## 3. Khởi chạy ứng dụng (Run)

Bạn có thể chạy ứng dụng theo một trong các cách sau:

1. Chạy trực tiếp script:
   ```cmd
   run.bat
   ```
2. Hoặc mở file `.exe` đã build:
   `bin\Release\net8.0-windows\win-x64\publish\AntigravityAutoPilot.exe`
3. Hoặc chạy lệnh .NET:
   ```bash
   dotnet run
   ```

---

## 4. Điều khiển Start / Pause

- **START**: Nhấp nút **▶ START** trên giao diện hoặc nhấn phím tắt `Ctrl + Shift + F11`.
- **PAUSE / EMERGENCY STOP**: Nhấp nút **⏸ PAUSE** hoặc nhấn phím tắt `Ctrl + Shift + F12`.
- Khi Antigravity IDE xuất hiện, ứng dụng hiển thị trạng thái `Antigravity: ● CONNECTED`.
- Khi Antigravity IDE chưa mở hoặc bị đóng, ứng dụng tự động chuyển sang `Antigravity: ○ Waiting for Antigravity...` và liên tục kiểm tra định kỳ để kết nối lại.

---

## 5. Phím tắt toàn cầu (Global Hotkeys)

Các phím tắt hoạt động ở bất kỳ cửa sổ nào (kể cả khi Antigravity IDE đang active):

| Phím tắt | Chức năng | Mô tả |
| :--- | :--- | :--- |
| `Ctrl + Shift + F12` | **Emergency Pause** | Dừng khẩn cấp toàn bộ hành động tự động click. Trạng thái chuyển thành `PAUSED`. |
| `Ctrl + Shift + F11` | **Resume / Start** | Tiếp tục chu trình auto pilot. |

---

## 6. Các chế độ hoạt động (Operating Modes)

Ứng dụng cung cấp 3 chế độ:

### 🛡️ SAFE Mode
- Chỉ tự động chấp nhận:
  - `Accept` (chấp nhận file edit, code changes)
  - `Accept changes`
  - `Accept all`
  - `Continue`
  - `Submit`
- **Không bao giờ tự phê duyệt terminal command** (`Run`, `Run command`, `Allow` đều bị chặn).

### ⚙️ NORMAL Mode (Mặc định khuyên dùng)
- Cho phép toàn bộ Whitelist:
  - `Accept`, `Accept All`, `Continue`, `Run`, `Submit`, `Retry`, `Allow`, `Apply`, `Confirm`.
- **Bảo vệ Terminal**: Mọi hành động `Run` / terminal command bắt buộc phải qua bộ lọc kiểm tra câu lệnh phá hủy (`SafetyEngine` & `CommandAnalyzer`).

### 🚀 FULL AUTO Mode
- Tự động click toàn bộ whitelist với tốc độ tối đa.
- **Lưu ý an toàn**: Blacklist từ khóa và Destructive-command protection **LUÔN LUÔN HOẠT ĐỘNG**, không một chế độ nào có thể tắt tính năng bảo vệ an toàn này.

---

## 7. Danh sách Button Whitelist

Bạn có thể bật/tắt từng nút trực tiếp trên Dashboard hoặc thêm từ khóa riêng trong tab **Settings**:

- `Accept` (chấp nhận thay đổi file)
- `Accept All` (chấp nhận tất cả thay đổi)
- `Continue` (tiếp tục phản hồi/chu trình)
- `Run` (chạy command đã qua kiểm duyệt an toàn)
- `Submit` (gửi output/prompt kế tiếp của agent)
- `Retry` (thử lại khi gặp sự cố mạng hoặc lỗi tạm thời)
- `Allow` (cấp quyền truy cập tool an toàn)
- `Apply` (áp dụng diff / patch)
- `Confirm` (xác nhận tiếp tục)
- `Auto Answer (Option 1)` (Tự động nhận diện form câu hỏi trắc nghiệm `ask_question`, tự chọn Option 1 / Yes / Allow, và tự động kích hoạt nút `Submit`)

---

## 8. Blacklist tuyệt đối (Absolute Blacklist)

Blacklist có độ ưu tiên cao hơn Whitelist. Bất kỳ nút bấm nào chứa các từ sau sẽ **KHÔNG BAO GIỜ** được tự click:

```text
Reject
Decline
Delete
Remove
Discard
Cancel
Reset
Terminate
Stop
Kill
Erase
Drop
Revert
Undo All
```

*Ví dụ*: Nút có nhãn `Accept Delete` sẽ bị từ chối ngay lập tức vì chứa từ khóa `Delete`.

---

## 9. Bảo vệ an toàn Terminal (Terminal Safety)

Trước khi kích hoạt bất kỳ nút `Run` hoặc `Run command` nào, `CommandAnalyzer` sẽ duyệt ngược lên cây giao diện (các thẻ text, group, code block chứa lệnh terminal) để đọc nội dung lệnh.

Lệnh sẽ bị **CHẶN** nếu chứa bất kỳ mẫu nào sau đây:

- `rm -rf`, `rm -r`
- `del /s`, `rmdir /s`
- `Remove-Item -Recurse`, `Remove-Item -r`
- `format` (định dạng ổ đĩa)
- `diskpart`
- `shutdown`, `reboot`
- `git reset --hard`
- `git clean -fd`, `git clean -fdx`
- `DROP DATABASE`, `DROP TABLE`
- `TRUNCATE TABLE`, `DELETE FROM`
- `docker system prune`, `docker volume prune`
- `kubectl delete`
- `terraform destroy`

Khi phát hiện:
1. Nút bấm bị bỏ qua, không được click.
2. Bộ đếm `COMMANDS BLOCKED` tăng lên.
3. Log ghi nhận: `BLOCKED: <tên câu lệnh> — potentially destructive command`.
4. Khay hệ thống (System Tray) phát thông báo cảnh báo màu vàng.

---

## 10. Smart Debounce & Timing

- **Scan Interval**: Mặc định **400 ms** (có thể chỉnh từ 100 ms tới 3000 ms).
- **Smart Debounce**: Mặc định **1200 ms** (có thể chỉnh từ 500 ms tới 3000 ms).
  - Ghi nhớ mã Runtime ID / Bounding Rectangle và thời điểm click của từng element.
  - Ngăn ngừa tình trạng spam click liên tục vào cùng một nút.
  - Sau mỗi lần click thành công, tool chờ một khoảng thời gian ngắn để giao diện IDE cập nhật trạng thái mới (Submit → Accept → Run → Continue).

---

## 11. Khay hệ thống & Ghi Log (Tray & Logging)

### Khay hệ thống (System Tray)
- Khi bấm nút `[X]` tắt cửa sổ, ứng dụng tự thu nhỏ xuống khay hệ thống (có thể tắt trong Settings).
- Menu chuột phải:
  - `Antigravity Auto Pilot` (thông tin)
  - `Start`
  - `Pause`
  - `Open`
  - `Exit`
- Nhấp đúp vào icon để mở lại cửa sổ chính.

### Ghi Log (Activity Log)
- Tab **Activity Log** hiển thị tối đa 1000 sự kiện gần nhất với nhãn màu trực quan (Xanh: Click thành công, Vàng: Cảnh báo/Pause, Đỏ: Chặn lệnh nguy hiểm).
- Hỗ trợ nút `Clear Log` và `Open Log File`.
- Tự động lưu file nhật ký theo ngày tại: `logs/YYYY-MM-DD.log`.

---

## 12. Hệ thống thông báo âm thanh (done.mp3 & submit.mp3)

Auto Pilot tích hợp hệ thống thông báo âm thanh 2 cấp độ giúp người dùng theo dõi tiến trình làm việc mà không cần nhìn chằm chằm vào màn hình:

### A. Thông báo duyệt Submit câu hỏi (submit.mp3)
Mỗi khi Auto Pilot tự động tick chọn phương án trả lời (Option 1 / Yes / Allow) và bấm nút **Submit** trên form câu hỏi tương tác (`ask_question`):
1. Phát ngay âm thanh `submit.mp3` để báo hiệu câu trả lời đã được nạp thành công vào Antigravity.
2. Thao tác này **không** kích hoạt thông báo hoàn tất tác vụ, đảm bảo Antigravity tiếp tục thực hiện luồng công việc mượt mà.
3. Bật/tắt bằng checkbox `Play submit.mp3 automatically when Submit is approved` trên Dashboard hoặc tab Settings, hỗ trợ nút **🔊 Test submit.mp3**.

### B. Thông báo hoàn tất toàn bộ tác vụ (done.mp3)
Khi Antigravity IDE hoàn thành toàn bộ lượt sinh code, chạy tool hoặc kết thúc nhiệm vụ:
1. `AutomationScanner` giám sát sự biến mất của nút "Stop generating" / "Stop" và xác nhận hệ thống duy trì trạng thái rảnh ổn định (idle >= 2.0s) mà không còn câu hỏi hoặc nút chờ duyệt nào.
2. Tự động phát âm thanh thông báo từ file `done.mp3`.
3. Gửi thông báo balloon ra khay hệ thống: `Antigravity IDE đã hoàn thành tác vụ!`.
4. Ghi nhận sự kiện vào Activity Log.

### C. Tùy chỉnh âm lượng & file âm thanh
- **Thử nghiệm nhanh**: Bấm nút **🔊 Test done.mp3** hoặc **🔊 Test submit.mp3** trên Dashboard hoặc tab Settings.
- **Âm lượng**: Điều chỉnh thanh trượt **Notification Volume** từ 0% đến 100% (áp dụng chung cho các loại âm thanh).
- **Đường dẫn file tùy chỉnh**: Mặc định là `done.mp3` và `submit.mp3` nằm cùng thư mục dự án / thực thi. Có thể chỉ định đường dẫn tuyệt đối hoặc tương đối trong tab Settings.

---

## 13. Cấu trúc mã nguồn (Architecture)

```text
tools/antigravity_auto_pilot/
├── UI/
│   ├── MainWindow.xaml         # Giao diện Dark Theme hiện đại
│   └── MainWindow.xaml.cs      # Xử lý sự kiện UI, Dispatcher & data-binding
├── Core/
│   ├── AntigravityDetector.cs  # Tìm kiếm Process, HWND và AutomationElement của Antigravity
│   ├── AutomationScanner.cs    # Vòng lặp quét bất đồng bộ (background Task, CancellationToken)
│   ├── ActionEngine.cs         # Thực thi InvokePattern, Debounce cache & Fallback click
│   ├── SafetyEngine.cs         # Bộ quy tắc Blacklist, Whitelist và Chế độ hoạt động
│   └── CommandAnalyzer.cs      # Phân tích ngữ cảnh & nhận diện câu lệnh terminal phá hủy
├── Models/
│   ├── AppSettings.cs          # Mô hình cấu hình lưu xuống settings.json
│   ├── AutomationAction.cs     # Thông tin phần tử UI và hành động
│   ├── LogEntry.cs             # Dữ liệu dòng nhật ký
│   └── Enums.cs                # Enum OperatingMode, EngineStatus, ActionKind
├── Services/
│   ├── SettingsService.cs      # Đọc/ghi cấu hình JSON
│   ├── LogService.cs           # Quản lý log bộ nhớ & ghi file nhật ký
│   ├── HotkeyService.cs        # Đăng ký phím tắt toàn cầu Win32 RegisterHotKey
│   └── TrayService.cs          # Quản lý biểu tượng và thông báo khay hệ thống
├── settings.json               # Cấu hình lưu tự động
├── run.bat                     # Script chạy nhanh
└── README.md                   # Tài liệu hướng dẫn
```

---

## 13. Khắc phục sự cố (Troubleshooting)

### Tool hiển thị "Waiting for Antigravity..." dù Antigravity đang mở
- Đảm bảo Antigravity IDE đã được khởi động hoàn toàn.
- Nếu Antigravity chạy với quyền Administrator (`Run as Administrator`), Antigravity Auto Pilot cũng cần được chạy dưới quyền Administrator để Windows UI Automation có quyền truy cập vào cây giao diện của tiến trình đặc quyền cao hơn.

### Nút không tự click được
- Kiểm tra xem nút đó có nằm trong Whitelist đã bật hay không.
- Kiểm tra xem tên nút có chứa từ khóa nào nằm trong Blacklist hay không.
- Nếu nút sử dụng custom styling không hỗ trợ `InvokePattern`, bạn có thể vào tab **Settings** và bật tùy chọn:
  `Allow physical mouse click fallback`.

### Kiểm tra file Log
- Xem trực tiếp trên tab **Activity Log** hoặc nhấn nút **Open Log File** để xem file nhật ký chi tiết tại thư mục `logs/`.
