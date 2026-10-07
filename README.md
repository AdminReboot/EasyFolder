# Easy Folder

Trình quản lý thư mục nhiều khung cho Windows, viết để thay Q-Dir ở đúng một việc: **mở nhiều thư mục cùng lúc và nhớ chính xác chúng nằm ở đâu**, kể cả khi ổ đĩa chưa sẵn sàng lúc mới bật máy.

## Vì sao có phần mềm này

Với Q-Dir, nếu mở chương trình khi Google Drive (hoặc ổ mạng) chưa mount xong, các khung trỏ tới ổ đó bị lỗi và danh sách thư mục đã lưu bị mất.

Easy Folder xử lý khác:

- Đường dẫn đã lưu **không bao giờ bị thay** chỉ vì thư mục tạm thời không truy cập được.
- Tab trỏ tới thư mục chưa sẵn sàng hiện màn hình chờ, tự thử lại mỗi 3 giây và mở ngay khi ổ đĩa xuất hiện.
- Việc kiểm tra ổ đĩa chạy ở luồng nền nên cửa sổ không bị treo khi ổ chậm phản hồi.
- File dữ liệu được ghi kiểu nguyên tử (ghi file tạm rồi thay thế), có bản `.bak` và bản sao lưu theo ngày (giữ 14 bản). Nếu file chính hỏng, chương trình tự đọc từ bản sao lưu.

## Tính năng

- 1, 2, 3, 4 hoặc 6 khung trong một cửa sổ; kéo vách ngăn để đổi kích thước.
- Mỗi khung có nhiều tab; mỗi tab là một khung Explorer gốc của Windows, nên menu chuột phải, kéo thả, sao chép/dán, đổi tên, biểu tượng trạng thái Google Drive/OneDrive đều hoạt động như File Explorer.
- Tự lưu liên tục: bố cục, các tab, tab đang chọn, vị trí vách ngăn, vị trí và kích thước cửa sổ.
- Phiên có tên: lưu nhiều bộ thư mục (ví dụ theo dự án) và chuyển qua lại từ menu **Phiên**.
- Yêu thích: đánh dấu thư mục hay dùng, mở nhanh từ menu (giữ Ctrl để mở trong tab mới).
- Nút trên mỗi khung: đổi kiểu hiển thị (chi tiết, danh sách, biểu tượng, ô xếp, nội dung), tạo thư mục mới, tạo file văn bản mới; item vừa tạo vào ngay chế độ đổi tên.
- Ô địa chỉ có gợi ý; dán đường dẫn tới một file sẽ mở thư mục chứa file đó.
- Tuỳ chọn khởi động cùng Windows.

## Phím tắt

| Phím | Chức năng |
| --- | --- |
| Ctrl+T, bấm đúp khoảng trống cạnh các tab | Tab mới (cùng thư mục đang xem) |
| Bấm đúp ô đường dẫn | Chọn toàn bộ đường dẫn |
| Ctrl+W, chuột giữa lên tab | Đóng tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Chuyển tab |
| Alt+← / Alt+→, nút bên hông chuột | Quay lại / tiến tới |
| Alt+↑ | Lên thư mục cha |
| Ctrl+L, Alt+D, F4 | Tới ô địa chỉ |
| Ctrl+S | Lưu phiên |
| F5 | Làm mới |

## Phiên hoạt động thế nào

- Trạng thái đang mở luôn được tự lưu và khôi phục ở lần chạy sau, không cần làm gì.
- **Phiên có tên** là một bản chụp bố cục. Sau khi sắp xếp lại thư mục, bấm Ctrl+S để cập nhật phiên đang dùng; nếu không bấm, phiên đã lưu vẫn giữ nguyên như lúc lưu.

## Cài đặt

Cần Windows 10/11 và [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

Tải `EasyFolder.exe` ở trang [Releases](https://github.com/AdminReboot/EasyFolder/releases/latest), đặt vào một thư mục bất kỳ có quyền ghi rồi chạy. Chỉ có một file, không cần cài đặt.

## Tự cập nhật

- Mỗi lần mở, chương trình hỏi GitHub xem có bản phát hành mới hơn không. Nếu có, nó hỏi bạn; đồng ý thì nó tải file mới, thay file đang chạy và tự khởi động lại, mở lại đúng các thư mục đang mở.
- Từ chối một bản thì bản đó không được hỏi lại lúc mở; vẫn cập nhật được bằng **Trợ giúp → Kiểm tra cập nhật…**
- Tắt kiểm tra tự động ở **Tuỳ chọn → Tự kiểm tra bản cập nhật khi mở**.
- Cập nhật không cần giao diện: `EasyFolder.exe --update` (mã thoát 0 = đã cập nhật, 1 = không có bản mới, 2 = lỗi).
- File tải về được đối chiếu kích thước và mã SHA-256 do GitHub công bố trước khi thay.

## Tự build

```bash
git clone https://github.com/AdminReboot/EasyFolder.git
cd EasyFolder
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

Phát hành bản mới: tăng `<Version>` trong `EasyFolder.csproj`, commit, push rồi chạy `tools\release.ps1`.

## Dữ liệu lưu ở đâu

Mặc định: `%APPDATA%\EasyFolder\data.json` (kèm `data.json.bak` và thư mục `backups`).

Chế độ portable: đặt một file `data.json` cạnh `EasyFolder.exe`, chương trình sẽ dùng thư mục đó thay cho `%APPDATA%`. Bản portable có dữ liệu riêng nên chạy song song được với bản thường.

## Cấu trúc mã nguồn

| File | Nội dung |
| --- | --- |
| `MainForm.cs` | Cửa sổ chính, bố cục khung, menu, phiên, phím tắt |
| `PaneControl.cs` | Một khung: thanh điều hướng, ô địa chỉ, các tab |
| `FolderView.cs` | Một tab: khung Explorer nhúng và cơ chế chờ/thử lại |
| `AppData.cs` | Mô hình dữ liệu, đọc/ghi và sao lưu |
| `Updater.cs` | Kiểm tra và cài bản mới từ GitHub Releases |
| `Native.cs` | Khai báo Win32/COM (`IExplorerBrowser`) |

## Giấy phép

MIT
