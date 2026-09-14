namespace Other;

internal static class UiTooltipGuidance
{
    public static string ForSlider(string title, string? tooltip)
    {
        string description = string.IsNullOrWhiteSpace(tooltip)
            ? "Điều chỉnh giá trị của cài đặt này."
            : tooltip.Trim();

        if (description.Contains("Tăng:", StringComparison.OrdinalIgnoreCase)
            && description.Contains("Giảm:", StringComparison.OrdinalIgnoreCase))
            return description;

        var (increase, decrease) = GetEffects(title);
        return $"{description}\n\nTăng: {increase}\nGiảm: {decrease}";
    }

    private static (string Increase, string Decrease) GetEffects(string title)
    {
        string key = title.ToLowerInvariant();

        if (key.Contains("missing frame"))
            return ("chịu được nhiều khung hình không thấy mục tiêu hơn, ít nhả khóa hơn nhưng có thể bám mục tiêu đã mất lâu hơn.",
                    "nhả mục tiêu bị mất sớm hơn, phản ứng nhanh hơn nhưng dễ mất khóa khi nhận diện chập chờn.");
        if (key.Contains("frame age"))
            return ("cho phép dữ liệu mục tiêu cũ tồn tại lâu hơn, ổn định hơn nhưng có thể dùng vị trí đã lỗi thời.",
                    "loại dữ liệu cũ sớm hơn, cập nhật chặt hơn nhưng dễ ngắt theo dõi khi FPS không đều.");
        if (key.Contains("smooth"))
            return ("chuyển động mượt và ít rung hơn, đổi hướng chậm hơn.",
                    "bám đổi hướng nhanh hơn, nhưng có thể rung hoặc giật nhiều hơn.");
        if (key.Contains("confidence") || key.Contains("threshold"))
            return ("lọc nghiêm hơn, giảm nhận nhầm nhưng có thể bỏ sót mục tiêu.",
                    "nhạy hơn và dễ bắt mục tiêu hơn, nhưng nguy cơ nhận nhầm tăng.");
        if (key.Contains("prediction distance") || key.Contains("lead multiplier") || key.Contains("lead time") || key.Contains("prediction time"))
            return ("dẫn tâm xa hơn theo chuyển động; hữu ích cho mục tiêu nhanh nhưng dễ vượt quá mục tiêu.",
                    "dẫn tâm ít hơn; ổn định với mục tiêu chậm nhưng có thể bị trễ phía sau.");
        if (key.Contains("jitter"))
            return ("thêm nhiều độ rung/ngẫu nhiên hơn vào đường chuột.",
                    "đường chuột đều và ổn định hơn.");
        if (key.Contains("sensitivity"))
            return ("chuột tiến đến mục tiêu mạnh và nhanh hơn, nhưng dễ quá đà.",
                    "chuột di chuyển nhẹ và chậm hơn, chính xác hơn nhưng bám mục tiêu chậm.");
        if (key.Contains("y offset (%)"))
            return ("điểm ngắm dịch lên cao hơn trong khung mục tiêu.",
                    "điểm ngắm dịch xuống thấp hơn trong khung mục tiêu.");
        if (key.Contains("x offset (%)"))
            return ("điểm ngắm dịch sang phải trong khung mục tiêu.",
                    "điểm ngắm dịch sang trái trong khung mục tiêu.");
        if (key.Contains("offset"))
            return ("dịch điểm ngắm theo chiều dương của trục tương ứng.",
                    "dịch điểm ngắm theo chiều âm của trục tương ứng.");
        if (key.Contains("delay") || key.Contains("interval") || key.Contains("duration") || key.Contains("reset time") || key.EndsWith(" time") || key.Contains("thời gian"))
            return ("chờ lâu hơn, ổn định hơn nhưng phản hồi chậm hơn.",
                    "phản hồi nhanh hơn, nhưng có thể kích hoạt sớm hoặc thiếu ổn định.");
        if (key.Contains("opacity"))
            return ("hiển thị đậm và rõ hơn.", "hiển thị trong suốt và ít che màn hình hơn.");
        if (key.Contains("radius"))
            return ("góc hiển thị bo tròn hơn.", "góc hiển thị vuông hơn.");
        if (key.Contains("thickness"))
            return ("viền dày và dễ thấy hơn.", "viền mảnh và ít che nội dung hơn.");
        if (key.Contains("size") || key.Contains("width") || key.Contains("height") || key.Contains("kích thước"))
            return ("phạm vi hoặc thành phần hiển thị lớn hơn.", "phạm vi hoặc thành phần hiển thị nhỏ hơn.");
        if (key.Contains("step"))
            return ("mỗi lần điều chỉnh thay đổi nhiều hơn, nhanh nhưng khó tinh chỉnh.",
                    "mỗi lần điều chỉnh thay đổi ít hơn, chậm nhưng tinh chỉnh chính xác hơn.");
        if (key.Contains("force") || key.Contains("recoil") || key.Contains("pull") || key.Contains("lực"))
            return ("tăng lực bù/ghì tâm.", "giảm lực bù/ghì tâm.");

        return ("tác động của cài đặt mạnh hơn hoặc ngưỡng lớn hơn.",
                "tác động của cài đặt nhẹ hơn hoặc ngưỡng nhỏ hơn.");
    }
}
