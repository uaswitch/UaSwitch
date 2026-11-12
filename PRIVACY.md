# Конфіденційність UaSwitch

*English version below.*

UaSwitch працює на рівні клавіатури, тому бачить усе, що ви набираєте. Ось що програма робить і чого не робить з цими даними.

## Набраний текст

- **Лише в пам'яті.** Щоб виправляти розкладку й ставити коми, програма тримає в пам'яті поточне слово й кілька попередніх слів речення. Цей буфер очищається, коли ви клацаєте мишею, перемикаєте вікно або починаєте нове речення. На диск він не записується ніколи.
- **Поля паролів.** У полях пароля (браузери, Telegram, звичайні вікна Windows) програма нічого не виправляє — навіть після «Показати пароль». «Слова» з цифрами чи символами (`Qwe12rty`, `p@ss`) теж не чіпає.
- **Журнал роботи** містить лише технічні події («запущено», «хуки встановлено», «поле пароля»). Набраний текст у журнал не потрапляє. Журнал можна відкрити з налаштувань і перевірити.

## Що зберігається на диску

Усе — у папці `%APPDATA%\UaSwitch`, звичайні текстові файли:

| Файл | Що в ньому |
|---|---|
| `settings.ini` | ваші налаштування (галочки) |
| `autoreplace.txt` | ваші скорочення для автозаміни |
| `exceptions.txt` | слова-винятки: додаються, коли ви скасовуєте виправлення клавішею Backspace |
| `uaswitch.log` | журнал роботи (без набраного тексту) |

Усі списки видно й можна редагувати в налаштуваннях. Щоб видалити все, закрийте програму й видаліть цю папку.

## Мережа

- **Ніякої телеметрії, аналітики, реклами чи облікових записів.**
- Єдиний запит в інтернет — **перевірка нової версії**: коли ви натискаєте «Перевірити оновлення» або якщо ввімкнете щоденну перевірку (за замовчуванням вимкнена). Це один запит до публічного API GitHub (`api.github.com`, сторінка останнього релізу). Жодних ваших даних він не містить; програма нічого не завантажує й не встановлює сама.
- Можна заблокувати UaSwitch у брандмауері Windows — програма повністю працюватиме без інтернету.

## Перевірка

Повний вихідний код відкритий (ліцензія MIT): https://github.com/uaswitch/UaSwitch. Програму можна зібрати самостійно скриптом `src/build.sh`. Мережеву активність можна перевірити, наприклад, Process Monitor чи TCPView.

Питання й зауваження — у [Issues](https://github.com/uaswitch/UaSwitch/issues).

Розробник: Ruslan Pichkar.

---

# UaSwitch privacy

UaSwitch works at the keyboard level, so it sees everything you type. This is what it does and does not do with that data.

## Typed text

- **Memory only.** To fix the layout and place commas, the app keeps the current word and the last few words of the sentence in memory. The buffer is cleared when you click the mouse, switch windows or start a new sentence. It is never written to disk.
- **Password fields.** In password fields (browsers, Telegram, standard Windows dialogs) the app does nothing, even after "Show password". It also never touches "words" that mix letters with digits or symbols (`Qwe12rty`, `p@ss`).
- **The log** contains only technical events ("started", "hooks installed", "password field"). Typed text is never logged. You can open the log from Settings and check.

## Stored on disk

Everything is in `%APPDATA%\UaSwitch`, as plain text files: `settings.ini` (your settings), `autoreplace.txt` (your auto-replace list), `exceptions.txt` (exception words, added when you undo a correction with Backspace), `uaswitch.log` (technical log, no typed text). All lists are visible and editable in Settings. To remove everything, quit the app and delete this folder.

## Network

- **No telemetry, analytics, ads or accounts.**
- The only network request is the **update check**: when you click "Check for updates", or daily if you enable it (off by default). It is a single request to GitHub's public API (`api.github.com`, latest release) and contains none of your data. The app never downloads or installs anything by itself.
- You can block UaSwitch in Windows Firewall; it works fully offline.

## Verification

The full source code is open (MIT): https://github.com/uaswitch/UaSwitch. You can build it yourself with `src/build.sh` and watch its network activity with tools like Process Monitor or TCPView.

Questions and concerns: [Issues](https://github.com/uaswitch/UaSwitch/issues).

Developer: Ruslan Pichkar.
