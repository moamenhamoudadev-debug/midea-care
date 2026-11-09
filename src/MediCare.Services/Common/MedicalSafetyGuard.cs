using System;
using System.Collections.Generic;
using System.Linq;

namespace MediCare.Services.Common;

/// <summary>
/// Server-side deterministic medical safety guardrails.
/// Detects emergency red flags, rejects unsafe prescription requests,
/// and guarantees standard medical disclaimer attachments.
/// </summary>
public static class MedicalSafetyGuard
{
    private static readonly string[] EmergencyArabicKeywords =
    {
        "ألم في الصدر", "الم في الصدر", "ألم شديد بالصدر", "وجع في صدري", "جلطة", "سكته", "سكتة",
        "شلل", "تنميل نصفي", "نزيف حاد", "نزيف مستمر", "تشنج", "تشنجات", "فقدان وعي", "اغماء",
        "ضيق تنفس حاد", "مش قادر اتنفس", "اختناق", "كحة دم", "سعال دموي", "تسمم", "انتحار"
    };

    private static readonly string[] EmergencyEnglishKeywords =
    {
        "chest pain", "heart attack", "stroke", "paralysis", "loss of consciousness",
        "unconscious", "passed out", "severe bleeding", "coughing blood", "difficulty breathing",
        "cannot breathe", "can't breathe", "suffocating", "severe convulsion", "seizure",
        "poisoning", "suicide", "overdose"
    };

    private static readonly string[] PrescriptionArabicKeywords =
    {
        "اكتبلي علاج", "اكتب لي علاج", "اكتبلي دوا", "اكتب لي دواء", "روشتة", "جرعة الدواء",
        "ايه الجرعة", "عايز مضاد حيوي", "اديني مسكن", "جرعة الباراسيتامول"
    };

    private static readonly string[] PrescriptionEnglishKeywords =
    {
        "prescribe me", "prescribe medicine", "prescription for", "drug dosage",
        "what dosage", "give me antibiotics", "recommend a pill", "which medication should i take"
    };

    public static bool IsEmergencySymptom(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        var lower = message.Trim().ToLowerInvariant();
        return EmergencyArabicKeywords.Any(k => lower.Contains(k)) ||
               EmergencyEnglishKeywords.Any(k => lower.Contains(k));
    }

    public static bool IsMedicationOrPrescriptionRequest(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return false;

        var lower = message.Trim().ToLowerInvariant();
        return PrescriptionArabicKeywords.Any(k => lower.Contains(k)) ||
               PrescriptionEnglishKeywords.Any(k => lower.Contains(k));
    }

    public static string GetEmergencyGuidance(string? culture)
    {
        bool isArabic = culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ?? false;
        return isArabic
            ? "⚠️ تنبيه طوارئ عاجل: الأعراض التي وصفتها قد تشير إلى حالة طبية حرجة تتطلب تدخلاً فورياً. يرجى التوجه فوراً لأقرب قسم طوارئ في مستشفى أو الاتصال بالإسعاف (123 في مصر). لا تنتظر استشارة إلكترونية."
            : "⚠️ CRITICAL EMERGENCY NOTICE: The symptoms you described may indicate an urgent medical emergency. Please call local emergency services (123 in Egypt / 911) or proceed immediately to the nearest hospital Emergency Room without delay.";
    }

    public static string GetMedicationSafetyNotice(string? culture)
    {
        bool isArabic = culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ?? false;
        return isArabic
            ? "تنبيه أمان دوائي: المساعد الذكي غير مصرح له بوصف أدوية أو تعديل جرعات علاجية. استشارة الطبيب المرخص وفحصه السريري إلزامية لسلامتك قبل تناول أي علاج."
            : "Medication Safety Notice: This AI assistant is not permitted to prescribe medications, specify dosages, or modify treatment plans. A physical evaluation by a licensed doctor is required for safe prescribing.";
    }

    public static string GetStandardDisclaimer(string? culture)
    {
        bool isArabic = culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) ?? false;
        return isArabic
            ? "معلومات استرشادية فقط لتوجيهك للتخصص المناسب بالعيادة. هذا المساعد ليس طبيباً ولا يقدم تشخيصاً طبياً أو بديلاً عن الفحص السريري."
            : "For general educational and clinic routing purposes only. This AI assistant does not provide a medical diagnosis or replace professional clinical care.";
    }
}
