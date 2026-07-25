using ClubPay.Agent.Core.Contracts.Enums;

namespace ClubPay.Agent.Client.ViewModels;

/// <summary>CLAUDE.md: the person at the keyboard never sees technical text — this maps every
/// <see cref="LockCodeRejectionReason"/> to a short friendly uz/ru message for the LockScreen input.</summary>
public static class LockCodeMessages
{
    public const string GenericError = "Xatolik yuz berdi. Qaytadan urinib ko'ring · Произошла ошибка, попробуйте ещё раз";

    public static string For(LockCodeRejectionReason? reason) => reason switch
    {
        LockCodeRejectionReason.Expired => "Kod muddati tugagan · Срок кода истёк",
        LockCodeRejectionReason.WrongPc => "Bu kod boshqa kompyuter uchun · Код для другого компьютера",
        LockCodeRejectionReason.AlreadyUsed => "Bu kod avval ishlatilgan · Код уже использован",
        LockCodeRejectionReason.PcBusy => "Kompyuter hozir band · Компьютер сейчас занят",
        LockCodeRejectionReason.ServiceUnavailable => "Kod qabul qilinmayapti, administratorga murojaat qiling · Код не принимается, обратитесь к администратору",
        _ => "Kod noto'g'ri. Qaytadan urinib ko'ring · Неверный код, попробуйте ещё раз",
    };
}
