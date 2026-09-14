# AIOK V4 Final

Mã nguồn chính: https://github.com/hung12312311/AI_PUBG11111

## Build và chạy

Windows, .NET 8 SDK, cấu hình x64:

```powershell
dotnet restore Aimmy2.csproj
dotnet build Aimmy2.csproj -c Release -p:Platform=x64
```

Mở `bin/Build/CouldBeAimmyV2.exe`. Git giữ model và cấu hình đầu vào trong `bin/Build/bin`; thư viện NuGet và chương trình được tạo lại khi build. Giữ nguyên thư mục đầu ra khi chạy. TensorRT cần môi trường CUDA/TensorRT phù hợp; file engine phụ thuộc phần cứng và phiên bản runtime.

## Nội dung V4 Final

- Theo dõi mục tiêu ổn định hơn, khóa mục tiêu có định danh và xử lý an toàn khi mất frame.
- Tách riêng ngưỡng mất dấu của Sticky Aim và Kalman; tùy chọn con chỉ hiện khi tính năng cha được bật.
- Cập nhật capture, thông tin model, ONNX/TensorRT và bảng hiệu suất.
- Giao diện Việt/Anh, lưu kích thước cửa sổ và độ nhạy riêng theo capture → kích thước ảnh → model.
- Tooltip giải thích tác động khi tăng/giảm và chỉ hiển thị ngôn ngữ đang chọn.
- Bắn từng viên có 5 mức; phát tiếp theo dùng mức 5. Sửa trạng thái ghì liên tục và lực bù con lăn.
- Bỏ build/cache khỏi Git; vẫn giữ dữ liệu runtime và các bản backup trong lịch sử.

Xem [báo cáo cập nhật](MERGE_REPORT.md), [tài liệu](Documentation.md) và các báo cáo cụ thể trong `reports/`.

## Kiểm chứng và giới hạn

V4 Final biên dịch Release với 0 lỗi. Bộ kiểm tra tự động tracking, Kalman, WGC, tọa độ, kích thước model và ONNX đều chạy qua. Chưa xác nhận cảm giác WGC/ghì tâm trực tiếp trong mọi trò chơi và phần cứng. TensorRT, Razer, LG HUB và ddxoft vẫn phụ thuộc driver/môi trường của máy chạy.

Các bản cũ và nhánh backup trước V4 được giữ nguyên để có thể khôi phục; xem thêm trong MERGE_REPORT.md.
