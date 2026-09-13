using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace Other;

// Presentation only: dropdown values and persisted gameplay keys stay in English.
public sealed partial class UiLanguage : INotifyPropertyChanged
{
    public static UiLanguage Current { get; } = new();
    private readonly string path = Path.Combine(AppContext.BaseDirectory, "bin", "language.cfg");
    private string code = "en";
    private static bool initialized;
    private static readonly Dictionary<string, string> Vietnamese = new()
    {
            ["Settings Menu"] = "Cài đặt chung",
            ["Aim Assist"] = "Hỗ trợ ngắm",
            ["Constant AI Tracking"] = "Theo dõi AI liên tục",
            ["Sticky Aim"] = "Giữ mục tiêu",
            ["Sticky Aim Threshold"] = "Ngưỡng giữ mục tiêu",
            ["Target Lock Duration"] = "Thời gian khóa mục tiêu",
            ["Mouse Movement Method"] = "Phương thức di chuyển chuột",
            ["Movement Path"] = "Đường di chuyển",
            ["Detection Area Type"] = "Kiểu vùng nhận diện",
            ["Aiming Boundaries Alignment"] = "Vị trí ngắm",
            ["Mouse Sensitivity"] = "Độ nhạy chuột",
            ["Mouse Sensitivity (+/-)"] = "Độ nhạy chuột (+/-)",
            ["Mouse Jitter"] = "Độ rung chuột",
            ["Y Axis Percentage Adjustment"] = "Điều chỉnh trục Y theo phần trăm",
            ["X Axis Percentage Adjustment"] = "Điều chỉnh trục X theo phần trăm",
            ["Y Offset (Up/Down)"] = "Độ lệch Y (lên/xuống)",
            ["Y Offset (%)"] = "Độ lệch Y (%)",
            ["X Offset (Left/Right)"] = "Độ lệch X (trái/phải)",
            ["X Offset (%)"] = "Độ lệch X (%)",
            ["Predictions"] = "Dự đoán chuyển động",
            ["Prediction Method"] = "Phương pháp dự đoán",
            ["CA Lead Multiplier"] = "Hệ số đón đầu CA",
            ["Kalman Lead Time"] = "Thời gian đón đầu Kalman",
            ["WiseTheFox Lead Time"] = "Thời gian đón đầu WiseTheFox",
            ["Shalloe Lead Multiplier"] = "Hệ số đón đầu Shalloe",
            ["EMA Smoothening"] = "Làm mượt EMA",
            ["Auto Trigger"] = "Tự động bắn",
            ["Cursor Check"] = "Kiểm tra con trỏ",
            ["Spray Mode"] = "Bắn liên tục",
            ["Only When Held"] = "Chỉ khi giữ phím",
            ["Auto Trigger Delay"] = "Độ trễ tự động bắn",
            ["Weapon Slot System"] = "Hệ thống ô vũ khí",
            ["Weapon Recognition System"] = "Nhận diện Súng",
            ["Scope Recognition System"] = "Nhận diện Scope",
            ["Weapon Recognition"] = "Nhận diện vũ khí",
            ["Scope Recognition"] = "Nhận diện scope",
            ["Toggle Weapon Scan"] = "Bật/tắt quét vũ khí",
            ["Toggle Scope Scan"] = "Bật/tắt quét scope",
            ["Show Weapon + Scope Info"] = "Hiện TT Súng + Ống ngắm",
            ["Weapon + Scope Info Size"] = "Kích thước bảng Súng + Scope",
            ["Weapon + Scope Info Opacity"] = "Độ trong suốt bảng Súng + Scope",
            ["Enable Tab Reset"] = "Đặt lại khi nhấn Tab",
            ["Mouse Wheel Adjust"] = "Điều chỉnh bằng con lăn",
            ["Scope Image Size"] = "Kích thước ảnh ống ngắm",
            ["Weapon Image Size"] = "Kích thước ảnh tên súng",
            ["Weapon AI Confidence"] = "Độ tin cậy AI tên súng",
            ["Scope Capture Method"] = "Phương thức chụp ống ngắm",
            ["Weapon Recognition Method"] = "Phương pháp nhận diện súng",
            ["Scope Recognition Method"] = "Phương pháp nhận diện scope",
            ["Scope Scan Keybind"] = "Phím quét scope",
            ["AI Model"] = "Mô hình AI",
            ["Template Matching"] = "So khớp mẫu",
            ["ORB Feature Matching"] = "So khớp đặc trưng ORB",
            ["SIFT Feature Matching"] = "So khớp đặc trưng SIFT",
            ["Auto Hybrid"] = "Tự động kết hợp",
            ["Template Confidence Threshold"] = "Ngưỡng tin cậy so khớp mẫu",
            ["Template Scale Min"] = "Tỷ lệ mẫu nhỏ nhất",
            ["Template Scale Max"] = "Tỷ lệ mẫu lớn nhất",
            ["Template Scale Step"] = "Bước thay đổi tỷ lệ mẫu",
            ["Scale"] = "Tỷ lệ",
            ["Enable Template Multi-scale"] = "Dò template nhiều tỷ lệ",
            ["Enable Template Edge Matching"] = "So khớp đường viền template",
            ["Enable Template Alpha Mask"] = "Dùng vùng trong suốt template",
            ["Enable Template Color Mask"] = "Dùng vùng màu template",
            ["Weapon Template Confidence Threshold"] = "Ngưỡng tin cậy template Súng",
            ["Weapon Template Scale Min"] = "Tỷ lệ template Súng nhỏ nhất",
            ["Weapon Template Scale Max"] = "Tỷ lệ template Súng lớn nhất",
            ["Weapon Template Scale Step"] = "Bước tỷ lệ template Súng",
            ["Weapon Template Multi-scale"] = "Dò template Súng nhiều tỷ lệ",
            ["Weapon Template Edge Matching"] = "So khớp đường viền template Súng",
            ["Weapon Template Alpha Mask"] = "Dùng vùng trong suốt template Súng",
            ["Weapon Template Color Mask"] = "Dùng vùng màu template Súng",
            ["Scope Template Confidence Threshold"] = "Ngưỡng tin cậy template Scope",
            ["Scope Template Scale Min"] = "Tỷ lệ template Scope nhỏ nhất",
            ["Scope Template Scale Max"] = "Tỷ lệ template Scope lớn nhất",
            ["Scope Template Scale Step"] = "Bước tỷ lệ template Scope",
            ["Scope Template Multi-scale"] = "Dò template Scope nhiều tỷ lệ",
            ["Scope Template Edge Matching"] = "So khớp đường viền template Scope",
            ["Scope Template Alpha Mask"] = "Dùng vùng trong suốt template Scope",
            ["Scope Template Color Mask"] = "Dùng vùng màu template Scope",
            ["Template Manager"] = "Quản lý Template",
            ["Weapon + Scope Recoil"] = "Recoil theo Súng + Scope",
            ["Scope Confidence"] = "Độ tin cậy nhận diện ống ngắm",
            ["Weapon Scan Delay"] = "Độ trễ quét vũ khí",
            ["Tab Reset Adjust"] = "Mức đặt lại bằng Tab",
            ["FOV Config"] = "Cài đặt vùng ngắm",
            ["Dynamic FOV"] = "Vùng ngắm động",
            ["Third Person Support"] = "Hỗ trợ góc nhìn thứ ba",
            ["Virtual Crosshair"] = "Tâm ngắm ảo",
            ["Crosshair Size"] = "Kích thước tâm ngắm",
            ["FOV Style"] = "Kiểu vùng ngắm",
            ["FOV Size"] = "Kích thước vùng ngắm",
            ["Dynamic FOV Size"] = "Kích thước vùng ngắm động",
            ["ESP Config"] = "Cài đặt hiển thị ESP",
            ["Show Detected Player"] = "Hiện mục tiêu nhận diện",
            ["Show Detection Performance"] = "Hiện FPS và thời gian suy luận",
            ["Show FPS / Inference"] = "Hiện FPS / thời gian suy luận",
            ["Show AI Confidence"] = "Hiện độ tin cậy AI",
            ["Show Tracers"] = "Hiện đường nối mục tiêu",
            ["Tracer Position"] = "Vị trí đường nối",
            ["AI Confidence Font Size"] = "Cỡ chữ độ tin cậy AI",
            ["Corner Radius"] = "Độ bo góc",
            ["Border Thickness"] = "Độ dày viền",
            ["Opacity"] = "Độ đậm",
            ["Recoil Config"] = "Cài đặt ghì tâm",
            ["Scope Recoil Control"] = "Ghì tâm theo ống ngắm",
            ["Mouse Wheel Adjust Step"] = "Bước điều chỉnh con lăn",
            ["Select Scope"] = "Chọn ống ngắm",
            ["Fast Loot Config"] = "Cài đặt nhặt nhanh",
            ["Fast Loot"] = "Nhặt nhanh",
            ["Fast Loot Delay"] = "Độ trễ nhặt nhanh",
            ["Image Size"] = "Kích thước ảnh",
            ["Target Class"] = "Lớp mục tiêu",
            ["AI Confidence"] = "Độ tin cậy AI",
            ["Priority Aiming"] = "Ưu tiên ngắm",
            ["Model Settings General"] = "Cài đặt mô hình chung",
            ["Enable Model Switch Keybind"] = "Bật phím chuyển model",
            ["Collect Data While Playing"] = "Thu thập dữ liệu khi chơi",
            ["Auto Label Data"] = "Tự động gán nhãn dữ liệu",
            ["Mouse Background Effect"] = "Hiệu ứng nền theo chuột",
            ["UI TopMost"] = "Luôn hiển thị trên cùng",
            ["Debug Mode"] = "Chế độ chẩn đoán",
            ["Save Config"] = "Lưu cấu hình",
            ["Window Height"] = "Chiều cao cửa sổ",
            ["Window Width"] = "Chiều rộng cửa sổ",
            ["Screen Settings"] = "Cài đặt màn hình",
            ["Screen Capture Method"] = "Phương thức chụp màn hình",
            ["StreamGuard"] = "Ẩn cửa sổ khỏi bản ghi",
            ["Refresh Displays"] = "Làm mới danh sách màn hình",
            ["Theme Settings"] = "Cài đặt giao diện",
            ["Language"] = "Ngôn ngữ",
            ["Aim Config (Slot 1)"] = "Cài đặt ngắm (Model 1)",
            ["Aim Config (Slot 2)"] = "Cài đặt ngắm (Model 2)",
            ["Model AI (Slot 1)"] = "Model 1",
            ["Model AI (Slot 2)"] = "Model 2",
            ["Aim Menu"] = "Ngắm",
            ["Model Menu"] = "Model",
            ["Settings"] = "Cài đặt",
            ["About"] = "Giới thiệu",
            ["Save"] = "Lưu",
            ["Cancel"] = "Hủy",
            ["Close"] = "Đóng",
            ["Load Config"] = "Tải cấu hình",
    };
    static UiLanguage()
    {
        Vietnamese["Save Configuration"] = "Lưu cấu hình";
        Vietnamese["Height"] = "Chiều cao";
        Vietnamese["Active: "] = "Đang dùng: ";
        Vietnamese["Adjust: "] = "Điều chỉnh: ";
        Vietnamese["Primary Display Selected"] = "Đã chọn màn hình chính";
        Vietnamese["AI PRECISION TARGETING"] = "NHẬN DIỆN VÀ NGẮM BẰNG AI";
        Vietnamese["INITIALIZING AIMMY AI"] = "ĐANG KHỞI TẠO AIMMY AI";
        Vietnamese["Preparing benchmark"] = "Đang chuẩn bị đo hiệu năng";
        Vietnamese["Freshest detections. Uses more CPU/GPU."] = "Nhận diện cập nhật nhanh nhất, sử dụng CPU/GPU nhiều hơn.";
        Vietnamese["Good detection speed with reasonable load."] = "Tốc độ nhận diện tốt với tải xử lý vừa phải.";
        Vietnamese["Lowest CPU/GPU use while staying near 60 FPS, or 30 FPS if needed."] = "Giảm tải CPU/GPU, duy trì gần 60 FPS hoặc 30 FPS khi cần.";
        Vietnamese["Fastest, Balanced, and Lowest Resource picks are labeled"] = "Các lựa chọn Nhanh nhất, Cân bằng và Ít tài nguyên nhất được đánh dấu";
        Vietnamese["Aimmy will test image sizes, then recommend a size + FPS cap for your goal. Lower image size can cut resource use, but can lose detail on small targets."] = "Aimmy sẽ thử các kích thước ảnh và đề xuất kích thước cùng giới hạn FPS phù hợp. Ảnh nhỏ giúp giảm tải nhưng có thể mất chi tiết mục tiêu nhỏ.";
        Vietnamese["Failed to initialize Weapon Recognition. Please check your model settings."] = "Không thể khởi tạo nhận diện vũ khí. Hãy kiểm tra cài đặt mô hình.";
        Vietnamese["The ddxoft Virtual Input Driver requires Aimmy to be run as an administrator, please close Aimmy and run it as administrator to use this movement method."] = "Trình điều khiển ddxoft yêu cầu quyền quản trị. Hãy đóng Aimmy rồi chạy bằng quyền quản trị để sử dụng phương thức này.";
        Vietnamese["Yes"] = "Có";
        Vietnamese["No"] = "Không";
        Vietnamese["OK"] = "Đồng ý";
        Vietnamese["Unfortunately, LG HUB Mouse is not here."] = "Không tìm thấy chuột LG HUB.";
        Vietnamese["Unfortunately, LG HUB Mouse Movement mode cannot be ran sufficiently."] = "Không thể khởi tạo phương thức di chuyển chuột LG HUB.";
        Vietnamese["Memory Integrity is enabled. Please disable it to use LG HUB Mouse Movement mode."] = "Memory Integrity đang bật. Phương thức LG HUB hiện yêu cầu tắt tính năng này.";
        Vietnamese["Failed to initialize Razer mode."] = "Không thể khởi tạo phương thức Razer.";
        Vietnamese["Failed to recover rzctl.dll."] = "Không thể khôi phục rzctl.dll.";
        Vietnamese["Razer Synapse is not running. Do you have it installed?"] = "Razer Synapse chưa chạy. Bạn đã cài đặt chưa?";
        Vietnamese["Razer Synapse is not installed. Would you like to install it?"] = "Chưa cài Razer Synapse. Bạn có muốn cài đặt không?";
        Vietnamese["LG HUB is not running, is it installed?"] = "LG HUB chưa chạy. Bạn đã cài đặt chưa?";
        Vietnamese["Would you like to install it?"] = "Bạn có muốn cài đặt không?";
        Vietnamese["LG HUB install is improper, would you like to install it?"] = "LG HUB được cài đặt chưa đúng. Bạn có muốn cài lại không?";
        Vietnamese["Failed to load ddxoft virtual input driver."] = "Không thể nạp trình điều khiển đầu vào ddxoft.";
        Vietnamese["Error clearing media: "] = "Lỗi khi xóa ảnh hoặc video: ";
        Vietnamese["Error writing JSON, please note:"] = "Lỗi khi lưu cấu hình JSON:";
        Vietnamese["Error creating a required directory: "] = "Lỗi khi tạo thư mục cần thiết: ";
        Vietnamese["Error loading config, possibly outdated"] = "Lỗi khi tải cấu hình; cấu hình có thể đã cũ";
        Vietnamese["Startup animation failed: "] = "Không thể chạy hiệu ứng khởi động: ";
        Vietnamese["Launching main application..."] = "Đang mở ứng dụng chính...";
        Vietnamese["FOV"] = "Vùng ngắm (FOV)";
        Vietnamese["Sensitivity is saved separately for each capture method, image size and model."] = "Độ nhạy được lưu riêng theo từng phương thức chụp, kích thước ảnh và model.";
        Vietnamese["Focus Display"] = "Màn hình sử dụng";
        Vietnamese["Scope Model Location"] = "Tệp mô hình nhận diện ống ngắm";
        Vietnamese["Weapon Model Location"] = "Tệp mô hình nhận diện tên súng";
        Vietnamese["Hide to Tray"] = "Ẩn xuống khay hệ thống";
        Vietnamese["Inventory"] = "Túi đồ";
        Vietnamese["Frames"] = "Khung hình";
        Vietnamese["Thickness"] = "Độ dày";
        Vietnamese["% Confidence"] = "% độ tin cậy";
        Vietnamese["MButton"] = "Chuột giữa";
        Vietnamese["Left Alt"] = "Alt trái";
        Vietnamese["Backslash (\\)"] = "Gạch chéo ngược (\\)";
        Vietnamese["Delete"] = "Xóa (Delete)";
        Vietnamese["English"] = "Tiếng Anh";
        Vietnamese["Language"] = "Ngôn ngữ";
        Vietnamese["None"] = "Không";
        Vietnamese["All Picks"] = "Tất cả mục tiêu";
        Vietnamese["All picks"] = "Tất cả mục tiêu";
        Vietnamese["Absolute"] = "Tuyệt đối";
        Vietnamese["Adaptive"] = "Thích ứng";
        Vietnamese["Auto"] = "Tự động";
        Vietnamese["Manual"] = "Thủ công";
        Vietnamese["Mouse Event"] = "Sự kiện chuột";
        Vietnamese["SendInput"] = "Gửi đầu vào (SendInput)";
        Vietnamese["ddxoft Virtual Input Driver"] = "Trình điều khiển đầu vào ddxoft";
        Vietnamese["Razer Synapse (Require Razer Peripheral)"] = "Razer Synapse (cần thiết bị Razer)";
        Vietnamese["Linear"] = "Tuyến tính";
        Vietnamese["Cubic Bezier"] = "Đường cong Bézier bậc ba";
        Vietnamese["Exponential"] = "Hàm mũ";
        Vietnamese["Perlin Noise"] = "Nhiễu Perlin";
        Vietnamese["Closest to Center Screen"] = "Gần tâm màn hình nhất";
        Vietnamese["Closest to Mouse"] = "Gần con trỏ nhất";
        Vietnamese["Best Confidence"] = "Độ tin cậy cao nhất";
        Vietnamese["Constant Acceleration"] = "Gia tốc không đổi";
        Vietnamese["Kalman Filter"] = "Bộ lọc Kalman";
        Vietnamese["Shall0e's Prediction"] = "Dự đoán Shall0e";
        Vietnamese["wisethef0x's EMA Prediction"] = "Dự đoán EMA của wisethef0x";
        Vietnamese["Circle"] = "Hình tròn";
        Vietnamese["Rectangle"] = "Hình chữ nhật";
        Vietnamese["Square"] = "Hình vuông";
        Vietnamese["Top"] = "Trên";
        Vietnamese["Bottom"] = "Dưới";
        Vietnamese["Center"] = "Giữa";
        Vietnamese["Middle"] = "Giữa";
        Vietnamese["Left"] = "Trái";
        Vietnamese["Right"] = "Phải";
        Vietnamese["Head"] = "Đầu";
        Vietnamese["Body"] = "Thân";
        Vietnamese["head"] = "đầu";
        Vietnamese["body"] = "thân";
        Vietnamese["enemy"] = "địch";
        Vietnamese["Enemy"] = "Địch";
        Vietnamese["Cross"] = "Chữ thập";
        Vietnamese["Dot"] = "Chấm";
        Vietnamese["Crosshair"] = "Tâm ngắm";
        Vietnamese["Sensitivity"] = "Độ nhạy";
        Vietnamese["Sens"] = "Độ nhạy";
        Vietnamese["Jitter"] = "Độ rung";
        Vietnamese["Multiplier"] = "Hệ số";
        Vietnamese["Seconds"] = "Giây";
        Vietnamese["Milliseconds"] = "Mili giây";
        Vietnamese["Pixels"] = "Điểm ảnh";
        Vietnamese["Percent"] = "Phần trăm";
        Vietnamese["Amount"] = "Mức";
        Vietnamese["Radius"] = "Bán kính";
        Vietnamese["Offset"] = "Độ lệch";
        Vietnamese["Size"] = "Kích thước";
        Vietnamese["Brightness"] = "Độ sáng";
        Vietnamese["Media Control"] = "Điều khiển ảnh và video";
        Vietnamese["Theme Color"] = "Màu giao diện";
        Vietnamese["Current Theme Color"] = "Màu giao diện hiện tại";
        Vietnamese["FOV Color"] = "Màu vùng ngắm";
        Vietnamese["Crosshair Color"] = "Màu tâm ngắm";
        Vietnamese["Detected Player Color"] = "Màu mục tiêu nhận diện";
        Vietnamese["ESP Color"] = "Màu lớp phủ ESP";
        Vietnamese["Single Class Color"] = "Màu lớp đơn";
        Vietnamese["ONNX Body Color"] = "Màu thân (ONNX)";
        Vietnamese["ONNX Head Color"] = "Màu đầu (ONNX)";
        Vietnamese["Engine Body Color"] = "Màu thân (TensorRT)";
        Vietnamese["Engine Head Color"] = "Màu đầu (TensorRT)";
        Vietnamese["OVERLAY Color Picker"] = "Chọn màu lớp phủ";
        Vietnamese["Aim Keybind"] = "Phím ngắm";
        Vietnamese["Change Aim Keybind"] = "Đổi phím ngắm";
        Vietnamese["Second Aim Keybind"] = "Phím ngắm thứ hai";
        Vietnamese["Auto Click Keybind"] = "Phím bắn tự động";
        Vietnamese["Dynamic FOV Keybind"] = "Phím vùng ngắm động";
        Vietnamese["Emergency Stop Keybind"] = "Phím dừng khẩn cấp";
        Vietnamese["Weapon Scan Keybind"] = "Phím quét vũ khí";
        Vietnamese["Scope Scan Keybind"] = "Phím quét scope";
        Vietnamese["Weapon Slot 1 Keybind"] = "Phím vũ khí Model 1";
        Vietnamese["Weapon Slot 2 Keybind"] = "Phím vũ khí Model 2";
        Vietnamese["Model Switch Keybind"] = "Phím chuyển mô hình";
        Vietnamese["Recoil Toggle Keybind"] = "Phím bật/tắt ghì tâm";
        Vietnamese["Recoil Toggle"] = "Bật/tắt ghì tâm";
        Vietnamese["Fast Loot Keybind"] = "Phím nhặt nhanh";
        Vietnamese["Priority Key"] = "Phím ưu tiên";
        Vietnamese["Crosshair Hide Key 1"] = "Phím ẩn tâm ngắm 1";
        Vietnamese["AI Minimum Confidence"] = "Độ tin cậy AI tối thiểu";
        Vietnamese["Aim Config"] = "Cài đặt ngắm";
        Vietnamese["Aim only when Trigger Button is held"] = "Chỉ ngắm khi giữ phím kích hoạt";
        Vietnamese["SLOT 1"] = "Model 1";
        Vietnamese["Slot 1"] = "Model 1";
        Vietnamese["Slot 2"] = "Model 2";
        Vietnamese["Slot 1: "] = "Model 1: ";
        Vietnamese["Slot 2: "] = "Model 2: ";
        Vietnamese["Slot 1 %"] = "Model 1 %";
        Vietnamese["Slot 2 %"] = "Model 2 %";
        Vietnamese["Detected Scope"] = "Ống ngắm nhận diện";
        Vietnamese["Detected Scopes"] = "Ống ngắm nhận diện";
        Vietnamese["Scope 1 (Red Dot/1x)"] = "Model scope 1 (chấm đỏ/1x)";
        Vietnamese["Scope 2 (2x)"] = "Model scope 2 (2x)";
        Vietnamese["Scope 3 (3x)"] = "Model scope 3 (3x)";
        Vietnamese["Scope 4 (4x)"] = "Model scope 4 (4x)";
        Vietnamese["Scope 5 (6x)"] = "Model scope 5 (6x)";
        Vietnamese["Scope 6 (8x)"] = "Model scope 6 (8x)";
        Vietnamese["+ Add Loot Item"] = "+ Thêm vật phẩm";
        Vietnamese["Drag"] = "Kéo thả";
        Vietnamese["R-Click"] = "Chuột phải";
        Vietnamese["Loot Inventory X"] = "Tọa độ X túi đồ";
        Vietnamese["Loot Inventory Y"] = "Tọa độ Y túi đồ";
        Vietnamese["not set"] = "chưa đặt";
        Vietnamese["Set"] = "Đặt";
        Vietnamese["No File Located"] = "Chưa chọn tệp";
        Vietnamese["Drag & Drop Media Here"] = "Kéo thả ảnh hoặc video vào đây";
        Vietnamese["Accept"] = "Chấp nhận";
        Vietnamese["Open"] = "Mở";
        Vietnamese["Exit"] = "Thoát";
        Vietnamese["Exit Aimmy"] = "Thoát Aimmy";
        Vietnamese["Exit Application"] = "Thoát ứng dụng";
        Vietnamese["What would you like to do?"] = "Bạn muốn thực hiện thao tác nào?";
        Vietnamese["Minimize"] = "Thu nhỏ";
        Vietnamese["No Thanks"] = "Không, cảm ơn";
        Vietnamese["No thanks"] = "Không, cảm ơn";
        Vietnamese["Try Again"] = "Thử lại";
        Vietnamese["Run"] = "Chạy";
        Vietnamese["Run Again"] = "Chạy lại";
        Vietnamese["Run Check"] = "Kiểm tra";
        Vietnamese["Run Test"] = "Chạy kiểm tra";
        Vietnamese["Performance Helper"] = "Trợ lý hiệu năng";
        Vietnamese["Aimmy Performance Helper"] = "Trợ lý hiệu năng Aimmy";
        Vietnamese["Run Performance Helper?"] = "Chạy kiểm tra hiệu năng?";
        Vietnamese["Quick full-speed check"] = "Kiểm tra nhanh ở tốc độ tối đa";
        Vietnamese["WHAT DO YOU WANT?"] = "BẠN MUỐN ƯU TIÊN GÌ?";
        Vietnamese["WHAT HAPPENS"] = "CÁCH KIỂM TRA";
        Vietnamese["Fastest"] = "Nhanh nhất";
        Vietnamese["Balanced"] = "Cân bằng";
        Vietnamese["Lowest"] = "Thấp nhất";
        Vietnamese["LowestResources"] = "Ít tài nguyên nhất";
        Vietnamese["Apply Settings"] = "Áp dụng cài đặt";
        Vietnamese["Selected pair"] = "Cặp thông số đã chọn";
        Vietnamese["Selected Avg FPS"] = "FPS trung bình đã chọn";
        Vietnamese["Selected Cap"] = "Giới hạn đã chọn";
        Vietnamese["Avg CPU Load"] = "Tải CPU trung bình";
        Vietnamese["Avg GPU Load"] = "Tải GPU trung bình";
        Vietnamese["Running"] = "Đang chạy";
        Vietnamese["Starting..."] = "Đang bắt đầu...";
        Vietnamese["Complete"] = "Hoàn tất";
        Vietnamese["Applied"] = "Đã áp dụng";
        Vietnamese["Skipped"] = "Đã bỏ qua";
        Vietnamese["Benchmark stopped"] = "Đã dừng đo hiệu năng";
        Vietnamese["Stopping benchmark"] = "Đang dừng đo hiệu năng";
        Vietnamese["No stable sample"] = "Chưa có mẫu ổn định";
        Vietnamese["No stable AI-loop sample was captured."] = "Chưa thu được mẫu xử lý AI ổn định.";
        Vietnamese["No training frames are saved"] = "Không lưu ảnh huấn luyện";
        Vietnamese["Mouse movement and clicks stay off"] = "Không di chuyển hoặc nhấp chuột";
        Vietnamese["5s+ warmup + 10s timed AI loop per size"] = "Khởi động ít nhất 5 giây rồi đo xử lý AI 10 giây cho mỗi kích thước";
        Vietnamese["Benchmark Aimmy once and get a size + FPS cap for this PC."] = "Đo hiệu năng để chọn kích thước ảnh và giới hạn FPS phù hợp với máy.";
        Vietnamese["Select Fastest, Balanced, or Lowest Resource, then apply that pair."] = "Chọn Nhanh nhất, Cân bằng hoặc Ít tài nguyên nhất rồi áp dụng.";
        Vietnamese["Applying selected size and FPS cap..."] = "Đang áp dụng kích thước ảnh và giới hạn FPS...";
        Vietnamese["Could not apply the selected pair. Model settings were left unchanged."] = "Không thể áp dụng thông số đã chọn. Cài đặt mô hình chưa thay đổi.";
        Vietnamese["Close this helper or run the test again after the model settles."] = "Đóng cửa sổ này hoặc đo lại sau khi mô hình ổn định.";
        Vietnamese["Close this helper or try again after loading a model."] = "Đóng cửa sổ này hoặc thử lại sau khi tải mô hình.";
        Vietnamese["Closing will remind you later."] = "Đóng cửa sổ để được nhắc lại sau.";
        Vietnamese["Lower image size cuts load. Small or far targets can lose detail."] = "Ảnh nhỏ giảm tải xử lý nhưng có thể làm mất chi tiết mục tiêu nhỏ hoặc ở xa.";
        Vietnamese["Opening benchmark panel."] = "Đang mở bảng đo hiệu năng.";
        Vietnamese["Restoring the loaded model before closing."] = "Đang khôi phục mô hình trước khi đóng.";
        Vietnamese["Try again after the model finishes settling."] = "Thử lại sau khi mô hình ổn định.";
        Vietnamese["You can change these later in Model Settings."] = "Bạn có thể thay đổi sau trong cài đặt mô hình.";
        Vietnamese["Select Region"] = "Chọn vùng";
        Vietnamese["Region Selector"] = "Chọn vùng màn hình";
        Vietnamese["Click and drag to select a region. Press ESC to cancel."] = "Nhấn giữ và kéo để chọn vùng. Nhấn Esc để hủy.";
        Vietnamese["No displays detected"] = "Không tìm thấy màn hình";
        Vietnamese["Error"] = "Lỗi";
        Vietnamese["ERROR"] = "LỖI";
        Vietnamese["WARNING"] = "CẢNH BÁO";
        Vietnamese["INFO"] = "THÔNG TIN";
        Vietnamese["Information"] = "Thông tin";
        Vietnamese["Warning"] = "Cảnh báo";
        Vietnamese["Success"] = "Thành công";
        Vietnamese["Loaded"] = "Đã tải";
        Vietnamese["Disabled"] = "Đã tắt";
        Vietnamese["Enabled"] = "Đã bật";
        Vietnamese["Suggested"] = "Đề xuất";
        Vietnamese["Suggested Model"] = "Mô hình đề xuất";
        Vietnamese["Suggested Model - Aimmy"] = "Mô hình đề xuất - Aimmy";
        Vietnamese["A config already exists with the same name, would you like to overwrite it?"] = "Đã có cấu hình trùng tên. Bạn có muốn ghi đè không?";
        Vietnamese["Can this model be found in the \"Downloadable Models\" Menu?"] = "Mô hình này có trong mục tải mô hình không?";
        Vietnamese[" (Found in Downloadable Model menu)"] = " (có trong mục tải mô hình)";
        Vietnamese[" (Primary)"] = " (màn hình chính)";
        Vietnamese["Name"] = "Tên";
        Vietnamese["LAUNCHING INTERFACE"] = "ĐANG MỞ GIAO DIỆN";
        Vietnamese["FOV size when holding the Dynamic FOV key. Usually smaller for scoped aim."] = "Kích thước vùng ngắm khi giữ phím vùng ngắm động, thường nhỏ hơn khi dùng ống ngắm.";
        Vietnamese["Shape of the FOV overlay. Circle is most common."] = "Hình dạng lớp phủ vùng ngắm. Hình tròn thường được dùng nhất.";
        Vietnamese["[Emergency Stop Keybind] Disabled all AI features."] = "[Dừng khẩn cấp] Đã tắt tất cả tính năng AI.";
        Vietnamese["Configs Cục Bộ"] = "Cấu hình trên máy";
        Vietnamese["Tải Configs"] = "Tải cấu hình";
        Vietnamese["Tải Models"] = "Tải mô hình";
        Vietnamese["Models Cục Bộ"] = "Mô hình trên máy";
        Vietnamese["Có (Yes)"] = "Có";
        Vietnamese["Không (No)"] = "Không";
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Code => code;
    public string this[string key] => Text(key);
    public static string Translate(string key)
    {
        foreach(var guidance in EnglishGuidance) if(guidance.Value == key) return guidance.Key;
        if (Vietnamese.TryGetValue(key, out var value)) return value;
        foreach(var prefix in Vietnamese.Keys.Where(k => k.Length > 20 && (k.EndsWith(": ") || k.EndsWith(":"))))
            if(key.StartsWith(prefix, StringComparison.Ordinal)) return Vietnamese[prefix] + key[prefix.Length..];
        if(key.Contains('\n')) return string.Join("\n", key.Split('\n').Select(line => Translate(line.TrimEnd('\r'))));
        foreach (var slot in new[] { "1", "2" })
        {
            string prefix = "Slot " + slot + " ";
            if (key.StartsWith(prefix) && Vietnamese.TryGetValue(key[prefix.Length..], out value))
                return value;
        }
        var sensitivity = System.Text.RegularExpressions.Regex.Match(key, @"^Slot (\d+) Sens · (.+)$");
        if (sensitivity.Success) return "Độ nhạy · " + sensitivity.Groups[2].Value;
        var recoil = System.Text.RegularExpressions.Regex.Match(key, @"^Recoil Scope (\d+) (.+)$");
        if (recoil.Success)
        {
            string suffix = recoil.Groups[2].Value;
            string text = suffix switch { "Tap" => "Ghì khi bắn từng viên", "Tap Distance" => "Lực ghì từng viên", _ => suffix };
            text = text.Replace("Strength", "Lực ghì").Replace("Force", "Lực ghì").Replace("Time", "Thời gian");
            return "Model scope " + recoil.Groups[1].Value + " · " + text;
        }
        if (System.Text.RegularExpressions.Regex.IsMatch(key, @"\.(onnx|engine|trt|cfg|dll|exe)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return key;
        string result = key;
        foreach (var phrase in new[] { ("Aim Assist", "Hỗ trợ ngắm"), ("Weapon Recognition", "Nhận diện vũ khí"), ("Fast Loot", "Nhặt nhanh"), ("Screen Capture Method", "Phương thức chụp màn hình"), ("Slot", "Model"), ("slot", "Model"), ("Capture", "Vùng chụp"), ("Confidence", "Độ tin cậy"), ("Inventory", "Túi đồ"), ("Item", "Vật phẩm"), ("Up2", "Trên 2"), ("Up3", "Trên 3"), ("Down2", "Dưới 2"), ("Down3", "Dưới 3"), ("Up", "Trên"), ("Down", "Dưới"), ("Error loading", "Lỗi khi tải"), ("Failed to load", "Không thể tải"), ("Error", "Lỗi"), ("Warning", "Cảnh báo") })
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<![\w.])" + System.Text.RegularExpressions.Regex.Escape(phrase.Item1) + @"(?![\w.])", phrase.Item2);
        return result;
    }
    public static void Initialize()
    {
        if (initialized) return;
        initialized = true;
        try { Current.code = File.ReadAllText(Current.path).Trim() == "vi" ? "vi" : "en"; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Localize((FrameworkElement)sender)), true);
    }
    public void SetLanguage(string value)
    {
        Initialize();
        if (value != "vi" && value != "en") throw new ArgumentException("Unsupported language");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", value);
        File.Move(path + ".tmp", path, true);
        code = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        foreach (Window window in Application.Current.Windows) RefreshTree(window);
        foreach (var weak in rendered.ToArray()) if (weak.TryGetTarget(out var entry)) entry.Apply();
        rendered.RemoveAll(weak => !weak.TryGetTarget(out _));
    }
    public static void Localize(FrameworkElement element)
    {
        if (element.ToolTip is string tip && !BindingOperations.IsDataBound(element, FrameworkElement.ToolTipProperty))
            BindingOperations.SetBinding(element,FrameworkElement.ToolTipProperty,TextBinding(tip));
        if (element is TextBlock block) { Track(block); return; }
        // Never translate option content: existing selection handlers use it as an ID.
        for (DependencyObject? p = element; p != null; p = VisualTreeHelper.GetParent(p))
            if (p is ComboBox or ComboBoxItem or TextBox) return;
        DependencyProperty? property = element switch
        {
            TextBlock => TextBlock.TextProperty,
            Label or ButtonBase or ToolTip => ContentControl.ContentProperty,
            _ => null
        };
        if (property == null || BindingOperations.IsDataBound(element, property)) return;
        if (element.GetValue(property) is not string key || (Translate(key) == key && English(key) == key)) return;
        if (element is Label label && label.Name.EndsWith("Title"))
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding());
            text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
            label.ContentTemplate = new DataTemplate { VisualTree = text };
            label.Padding = new Thickness(10, 3, 10, 3);
        }
        BindingOperations.SetBinding(element, property, TextBinding(key));
    }
    private static Binding TextBinding(string key) => new(nameof(Code)) { Source=Current,Mode=BindingMode.OneWay,Converter=new TextConverter(key) };
    private sealed class TextConverter(string key) : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Text(key);
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    public static string Text(string value) => Current.Code == "vi" ? Translate(value) : English(value);
    public static string English(string value)
    {
        if(EnglishGuidance.TryGetValue(value,out var translated)) return translated;
        foreach(var pair in Vietnamese) if(pair.Value==value) return pair.Key;
        foreach(var prefix in EnglishGuidance.Keys.Where(k=>k.EndsWith(": ")))
            if(value.StartsWith(prefix)) return EnglishGuidance[prefix]+value[prefix.Length..];
        if(value.Contains('\n')) return string.Join("\n",value.Split('\n').Select(English));
        var label=System.Text.RegularExpressions.Regex.Match(value,@"^Slot [12] (.+)$");
        if(label.Success) return label.Groups[1].Value;
        var sens=System.Text.RegularExpressions.Regex.Match(value,@"^Slot [12] Sens · (.+)$");
        if(sens.Success) return "Sensitivity · "+sens.Groups[1].Value;
        return value;
    }
    private static readonly List<WeakReference<RenderedText>> rendered = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TextBlock, RenderedText> tracked = new();
    private static void Track(TextBlock block)
    {
        // Text is presentation only; never rewrite ComboBoxItem.Content or user input.
        for (DependencyObject? p = block; p != null; p = VisualTreeHelper.GetParent(p))
            if (p is TextBox || p is PasswordBox) return;
        tracked.GetValue(block, b => { var entry = new RenderedText(b); rendered.Add(new(entry)); return entry; }).Apply();
    }
    private sealed class RenderedText
    {
        private readonly TextBlock block;
        private string original;
        private string displayed;
        private bool applying;
        private bool attached;
        private readonly DependencyPropertyDescriptor descriptor = DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        public RenderedText(TextBlock value)
        {
            block = value; original = displayed = block.Text;
            Attach();
            block.Loaded += (_, _) => { Attach(); Apply(); };
            block.Unloaded += (_, _) => { if (attached) descriptor.RemoveValueChanged(block, Changed); attached = false; };
        }
        private void Attach() { if (!attached) { descriptor.AddValueChanged(block, Changed); attached = true; } }
        private void Changed(object? sender, EventArgs args)
        {
            if (applying) return;
            original = block.Text; Apply();
        }
        public void Apply()
        {
            if (applying) return;
            if (block.Text != displayed) original = block.Text;
            displayed = Text(original);
            if (block.Text == displayed) return;
            applying = true;
            try { block.SetCurrentValue(TextBlock.TextProperty, displayed); }
            finally { applying = false; }
        }
    }
    public static void RefreshTree(DependencyObject node)
    {
        if (node is ComboBox combo) PrepareDropdown(combo);
        if (node is FrameworkElement element) Localize(element);
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) RefreshTree(VisualTreeHelper.GetChild(node, i));
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ComboBox, object> preparedDropdowns = new();
    public static void PrepareDropdown(ComboBox combo)
    {
        combo.ItemTemplate ??= OptionTemplate();
        foreach (var item in combo.Items.OfType<ComboBoxItem>()) item.ContentTemplate ??= OptionTemplate();
        preparedDropdowns.GetValue(combo, control =>
        {
            ((System.Collections.Specialized.INotifyCollectionChanged)control.Items).CollectionChanged += (_, args) =>
            {
                if (args.NewItems == null) return;
                foreach (var item in args.NewItems.OfType<ComboBoxItem>()) item.ContentTemplate ??= OptionTemplate();
            };
            return new object();
        });
    }

    private static DataTemplate OptionTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.White);
        var binding = new MultiBinding { Converter = new OptionConverter() };
        binding.Bindings.Add(new Binding());
        binding.Bindings.Add(new Binding(nameof(Code)) { Source = Current });
        text.SetBinding(TextBlock.TextProperty, binding);
        return new DataTemplate { VisualTree = text };
    }
    private sealed class OptionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Text(values[0]?.ToString() ?? "");
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

}
