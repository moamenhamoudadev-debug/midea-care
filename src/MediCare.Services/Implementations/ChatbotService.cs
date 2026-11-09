using System.Text.RegularExpressions;
using MediCare.Data.UnitOfWork;
using MediCare.Services.Contracts;
using MediCare.Services.DTOs;

namespace MediCare.Services.Implementations;

public class ChatbotService : IChatbotService
{
    private readonly IUnitOfWork _unitOfWork;

    public ChatbotService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ChatbotResponseDto> ProcessMessageAsync(ChatbotRequestDto request)
    {
        var message = (request.Message ?? string.Empty).Trim().ToLowerInvariant();
        var isArabic = request.Culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true
                       || Regex.IsMatch(request.Message ?? "", @"[\u0600-\u06FF]");

        var response = new ChatbotResponseDto();
        string? matchedSpecialtyName = null;

        // Emergency signals take precedence over routine specialty suggestions.
        if (ContainsAny(message, "chest pain", "ألم في الصدر", "وجع في الصدر", "shortness of breath", "ضيق تنفس", "cannot breathe", "مش قادر اتنفس", "bleeding", "نزيف", "stroke", "جلطة", "numbness", "unconscious", "فقدان وعي", "faint", "إغماء"))
        {
            response.IsEmergency = true;
            response.EmergencyNotice = isArabic
                ? "تنبيه طوارئ: قد تكون هذه حالة طارئة. لا تعتمد على هذا المساعد لتقييمها؛ توجّه إلى أقرب قسم طوارئ أو اتصل بالإسعاف في مصر على 123."
                : "EMERGENCY NOTICE: This may be an emergency. Do not rely on this assistant to assess it. Go to the nearest emergency department or call Egypt's ambulance service at 123.";
            response.ReplyText = isArabic
                ? "اطلب المساعدة الطبية الطارئة الآن."
                : "Please seek emergency medical help now.";
            return response;
        }

        // 1. Symptom & Specialty Analysis
        if (ContainsAny(message, "قلب", "صدر", "ألم في الصدر", "نهجان", "ضغط", "ضربات", "خفقان", "heart", "chest", "palpitation", "cardio", "hypertension"))
        {
            matchedSpecialtyName = "Cardiology";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "بناءً على الأعراض المذكورة (ألم الصدر أو اضطراب ضربات القلب)، ننصح بمراجعة استشاري أمراض القلب والأوعية الدموية لإجراء فحص سريري وتخطيط للقلب."
                : "Based on the reported symptoms (chest discomfort, palpitations, or blood pressure concerns), we recommend consulting a Cardiology Specialist for comprehensive clinical assessment.";
        }
        else if (ContainsAny(message, "جلد", "بشرة", "حبوب", "طفح", "حكة", "تساقط", "شعر", "إكزيما", "skin", "rash", "acne", "hair", "derma", "itching", "eczema"))
        {
            matchedSpecialtyName = "Dermatology";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "قد يكون طبيب الجلدية هو التخصص المناسب للاستفسار عن هذه الأعراض. هذا اقتراح عام وليس تشخيصاً؛ الطبيب وحده يقيّم الحالة."
                : "A Dermatology specialist may be a suitable place to start. This is general guidance, not a diagnosis; a clinician must assess the condition.";
        }
        else if (ContainsAny(message, "طفل", "أطفال", "رضيع", "سخونية الطفل", "تطعيم", "حرارة طفلي", "child", "pediatric", "baby", "infant", "toddler"))
        {
            matchedSpecialtyName = "Pediatrics";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "يمكنك استشارة طبيب أطفال بشأن هذه الأعراض. هذا اقتراح عام وليس تشخيصاً أو تقييماً لمدى خطورة الحالة."
                : "A Pediatric specialist may be suitable for these concerns. This is general guidance, not a diagnosis or assessment of urgency.";
        }
        else if (ContainsAny(message, "عظام", "مفصل", "ركبة", "كسر", "ظهر", "فقرات", "عمود فقري", "bone", "joint", "ortho", "knee", "spine", "fracture", "back pain"))
        {
            matchedSpecialtyName = "Orthopedics";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "يمكنك استشارة طبيب عظام بشأن آلام المفاصل أو الظهر. هذا اقتراح عام ولا يحدد سبب الأعراض أو علاجها."
                : "An Orthopedics specialist may be suitable for joint or back symptoms. This does not identify their cause or recommend treatment.";
        }
        else if (ContainsAny(message, "باطنة", "معدة", "سكر", "قولون", "كبد", "إرهاق", "صداع", "حرارة", "سخونة", "stomach", "diabetes", "colon", "internal", "headache", "fatigue", "fever"))
        {
            matchedSpecialtyName = "General Internal Medicine";
            response.RecommendedSpecialization = matchedSpecialtyName;
            response.ReplyText = isArabic
                ? "قد يكون طبيب الباطنة مناسباً للاستفسار عن هذه الأعراض. هذا اقتراح عام وليس تشخيصاً أو توصية بفحوصات أو علاج."
                : "An Internal Medicine specialist may be a suitable place to start. This is not a diagnosis or a recommendation for tests or treatment.";
        }
        else if (ContainsAny(message, "إلغاء", "الغاء", "cancel"))
        {
            response.ReplyText = isArabic
                ? "يمكنك إلغاء موعدك بسهولة من صفحة 'مواعيدي' بشرط أن يتبقى أكثر من ساعتين على موعد الكشف، وسيتم تحرير الشريحة الزمنية تلقائياً."
                : "You can cancel your consultation at any time up to 2 hours prior to the scheduled slot from your 'My Appointments' page.";
            response.QuickReplies = isArabic
                ? new() { "استعراض الأطباء", "سعر الكشف", "طريقة الحجز" }
                : new() { "Find Doctors", "Consultation Fees", "How to Book" };
            return response;
        }
        else if (ContainsAny(message, "سعر", "اسعار", "أتعاب", "fee", "cost", "price"))
        {
            response.ReplyText = isArabic
                ? "رسوم الكشف تختلف حسب الطبيب. راجع صفحة الطبيب لمعرفة الرسوم المسجلة؛ الدفع في هذه النسخة تجريبي ولا يخصم أموالاً."
                : "Consultation fees vary by doctor. Check the doctor's page for the listed fee. Payments in this demo are simulated and do not charge money.";
            response.QuickReplies = isArabic
                ? new() { "أطباء القلب", "أطباء الباطنة", "أطباء الأطفال" }
                : new() { "Cardiology", "Internal Medicine", "Pediatrics" };
            return response;
        }
        else if (ContainsAny(message, "حجز", "احجز", "book", "appointment", "schedule"))
        {
            response.ReplyText = isArabic
                ? "لحجز موعد: توجه لصفحة 'ابحث عن طبيب'، اختر التخصص والطبيب المناسب، ثم اختر التاريخ والوقت المتاح واضغط 'حجز موعد'."
                : "To book an appointment: Navigate to 'Find Doctors', select your preferred specialist, choose an available date and time slot, and click 'Book Slot'.";
            response.QuickReplies = isArabic
                ? new() { "ابحث عن طبيب", "استشارة باطنة", "استشارة قلب" }
                : new() { "Find Doctors", "Book Cardiology", "Book Internal" };
            return response;
        }
        else if (ContainsAny(message, "سلام", "مرحبا", "أهلا", "اهلا", "صباح الخير", "مساء الخير", "hello", "hi", "hey"))
        {
            response.ReplyText = isArabic
                ? "أهلاً بك في MediCare. أستطيع شرح خطوات الحجز أو اقتراح تخصص عام. لا أقدم تشخيصاً أو علاجاً؛ استخدم بيانات تجريبية فقط. كيف أساعدك؟"
                : "Welcome to MediCare. I can explain booking steps or suggest a broad specialty. I do not diagnose or recommend treatment; use demo data only. How can I help?";
            response.QuickReplies = isArabic
                ? new() { "ألم في الصدر", "حساسية أو طفح جلدي", "كشف أطفال", "ألم في المفاصل", "استشارة باطنة" }
                : new() { "Chest discomfort", "Skin allergy", "Child fever", "Joint pain", "Internal Medicine" };
            return response;
        }
        else
        {
            response.ReplyText = isArabic
                ? "شكراً لتواصلك مع MediCare! هل يمكنك توضيح الأعراض التي تشعر بها أكثر (مثل ألم في الصدر، مشكلة جلدية، ألم مفاصل، أو كشف أطفال)؟"
                : "Thank you for reaching out to MediCare! Could you tell me more about your symptoms (e.g. chest discomfort, skin symptoms, joint pain, or child care)?";
            response.QuickReplies = isArabic
                ? new() { "ألم في الصدر", "طفح جلدي", "ألم في الركبة", "ارتفاع حرارة" }
                : new() { "Cardiology", "Dermatology", "Orthopedics", "General Internal" };
            return response;
        }

        // Fetch doctors for recommended specialty
        if (!string.IsNullOrEmpty(matchedSpecialtyName) && _unitOfWork.Doctors != null)
        {
            var allDoctors = await _unitOfWork.Doctors.GetApprovedDoctorsAsync(null);
            if (allDoctors != null)
            {
                var specialtyDocs = allDoctors
                    .Where(d => d.Specialization != null && d.Specialization.Name.Contains(matchedSpecialtyName, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .ToList();

                foreach (var doc in specialtyDocs)
                {
                    response.Doctors.Add(new ChatbotDoctorCardDto
                    {
                        Id = doc.Id,
                        FullName = doc.User != null ? doc.User.FullName : $"Doctor #{doc.Id}",
                        Specialization = doc.Specialization?.Name ?? matchedSpecialtyName,
                        Fee = doc.ConsultationFee,
                        AvatarUrl = doc.ProfileImageUrl
                    });
                }
            }
        }

        response.QuickReplies = isArabic
            ? new() { "حجز موعد", "سعر الكشف", "سياسة الإلغاء", "تخصص آخر" }
            : new() { "Book Appointment", "Consultation Fees", "Cancellation Policy", "Other Specialty" };

        return response;
    }

    private static bool ContainsAny(string source, params string[] terms)
    {
        return terms.Any(t => source.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}
