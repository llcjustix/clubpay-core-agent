# Static Launcher — xavfsiz deploy tartibi

V3 katalogi bo'lmaguncha launcher ro'yxati `appsettings.json`dagi `Launcher:Apps` orqali
boshqariladi. Bu fayl o'yinchi tomonidan emas, installer yoki klubning boshqariladigan deploy
paketi orqali yangilanadi.

## Launch profile

Har bir yozuvda barqaror `Id`, `Type`, `ExePath` va faqat shu o'yinga tegishli `ProcessNames`
bo'lishi shart. `ProcessNames`da `.exe` kengaytmasi yozilmaydi, masalan `cs2`, `dota2`, `TslGame`.
Ular o'yin ishga tushgandan keyin processni kuzatish va sessiya tugaganda faqat shu processlarni
yopish uchun ishlatiladi.

`SteamGame` uchun `SteamAppId` majburiy. Agent `Steam.exe -applaunch <SteamAppId>` argumentini o'zi
yasaydi; shu sabab app ID argument satriga qo'lda qayta yozilmaydi. Oddiy Win32 dastur `Executable`,
browser kiosk esa `Browser` turida qoladi.

```json
{
  "Id": "cs2",
  "Name": "Counter-Strike 2",
  "Type": "SteamGame",
  "ExePath": "C:\\Program Files (x86)\\Steam\\Steam.exe",
  "SteamAppId": 730,
  "ProcessNames": [ "cs2" ],
  "Category": "O'yin"
}
```

Profilni o'zgartirishdan oldin administrator test PKda o'yinning haqiqiy process nomini Task
Manager orqali tekshiradi. Noto'g'ri yoki juda keng nom (masalan, `steam`ni CS2 profiliga qo'shish)
o'yin tugaganini noto'g'ri aniqlashga olib keladi.

## Windows allowlist

Launcher UI faqat ko'rinish qatlami: u Windows'ning o'zi boshqa `.exe`larni ishga tushirishini
to'xtata olmaydi. Production kioskda alohida kiosk Windows akkaunti uchun Windows Assigned Access
multi-app siyosati va uning allowlist qoidalari qo'llanadi. Allowlistga Agent, har bir ruxsat etilgan
game launcher/game executable hamda ular uchun zarur yordamchi executablelar kiritiladi.

Siyosat agent orqali runtime'da o'zgartirilmaydi. U administrator tomonidan provisioning/MDM yoki
deploy paketi bilan beriladi, avval bitta test PKda tekshiriladi, keyin klub bo'yicha tarqatiladi.
Menejer akkaunti kiosk siyosatidan ajratilgan bo'ladi.

## Yangilash va rollback

1. Yangi profilni test PKda ishga tushiring va sessiya tugaganda faqat o'yin processlari yopilishini
   tekshiring.
2. `appsettings.json` hamda Windows allowlist qoidalarini bir xil reliz versiyasida tarqating.
3. Xato bo'lsa, avvalgi versiyalangan konfiguratsiya va siyosatga qayting; session davomida faylni
   almashtirmang.
