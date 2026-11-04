# HonestFlow 3.0 — аудит безопасности исходного кода

Дата: 27.10.2026. Режим: только чтение исходников и метаданных локальной Windows; исходный код, базы и production не изменялись. Это оценка текущих checkout, **не** утверждение о том, какой код и какие настройки уже работают на сервере.

| Компонент | Ветка / HEAD на момент аудита | Состояние |
|---|---|---|
| HonestFlow | `codex/remote-support` / `9f0ea3c` | 20 уже существовавших изменённых/новых путей |
| HonestLicenseServer | `main` / `13079e1` | 2 уже существовавших новых пути (`site/`, скрипт) |
| HonestDesk | `agent/wpf-admin-workflow` / `67aa038` | чистый checkout |

Проверены фактические цепочки WPF startup → Service resume/login → configuration/license → локальные операции; контроллеры API и политики авторизации; SQLite-модели и индексы; ключ подписи в HonestDesk; обновление и загрузка компонентов; журналы и тестовые сценарии. WinForms исключён из production WPF-сборки и не рассматривается как действующий интерфейс. Нагрузочные, эксплуатирующие и production-запросы не выполнялись. Наличие существующих тестов отмечено, но тесты в рамках этого read-only задания не запускались: фабрика тестов создаёт и удаляет временную SQLite-базу.

**Степень доказанности:** `Confirmed` означает прямой путь в исходниках (и, где указано, локальные ACL); `Likely` — необходима проверка в изолированном окружении; `Hypothesis` — недостаточно данных. Наличие риска в исходниках не доказывает его эксплуатацию в production. Оценки ниже — инженерная оценка риска, а не результат сертификации или автоматического сканера.

## A. Executive Summary

| Область | Оценка / 10 | Основание |
|---|---:|---|
| Общая безопасность | 4 | Сервер отделяет клиентские и административные API, проверяет подписанный grant, но сохранённые токены извлекаемы другим локальным пользователем, а DeviceId не является доказательством владения устройством. |
| Защита лицензирования | 5 | Подпись ECDSA и серверный `ServicePolicy` мешают подделать серверные полномочия. Локальный бинарник и clock/grace остаются под контролем владельца ПК; WPF не учитывает отдельные feature-флаги. |
| Безопасность API | 5 | Большинство критичных маршрутов проверяют active client/device/Service либо admin key. Есть переносимые bearer-токены, раскрытие секретов через configuration и отсутствие защиты от повторного использования refresh-семейства. |
| Offline grace | 3 | Подписанный срок ограничивает нормальное выполнение; локальный anchor времени можно сбросить при контроле ПК, а уже разрешённый runtime перепроверяется лишь через 60–75 минут. |
| Защита данных | 3 | HF использует `LocalMachine` DPAPI для сессии/идентификатора/clock; фактические файлы читаемы группой Users. Сервер хранит идентификационный код и CHZ-токен как текстовые поля. |
| Устойчивость к DoS | 3 | SQLite является общей точкой записи; login/install создают записи/состояние на неуспешных попытках, административные выборки без страниц; лимиты по IP конфликтуют с NAT. Массовая нагрузка не измерялась. |
| Безопасность обновлений | 4 | SHA-256 и размер проверяются до установки, но ожидаемый SHA берётся из той же административно изменяемой БД, отдельной проверки подписи издателя нет; скрипт применяет EXE из ProgramData. |

Ключевое разделение гарантий: **подделать подписанный grant без закрытого ECDSA-ключа по проверенным цепочкам нельзя; пропатчить локальный .NET-клиент и его UI на собственном ПК принципиально возможно.** Защищённые серверные действия после такого патча всё равно должны пройти `ServicePolicy`. Украденный bearer/refresh-токен или идентификационный код с DeviceId дают другой путь: сервер принимает их как полномочие, не проверяя физическую машину.

### Карта доверия

1. Free WPF: `App.xaml.cs:18-47` → публичная проверка обновления → `LocalApplicationBootstrap.cs:18-48` → локальный MainWindow; идентификатор устройства и лицензия здесь не создаются.
2. Service: `ApiAuthService.cs:35-65` получает токены по паролю и строке DeviceId; `AuthController.cs:27-75` связывает с Client/Device. Для Active Device HF получает configuration и `/api/license/current`, затем проверяет ECDSA и subject в `ApiLicenseManifestRepository.cs:35-93`.
3. Серверная лицензия: закрытый ключ читается **только в HonestDesk** (`LicenseGrantPublicationBuilder.cs:18-21,87-93`). `AdminController.cs:339-386` проверяет подпись до публикации; `LicenseController.cs:24-54` выдаёт последний проверенный grant для claim client/device. `ServiceEntitlementRequirement.cs:25-80` заново проверяет актуальную policy, статус, срок, subject и `Service` для сервисных маршрутов.
4. Локальная лицензия: `LicenseObservationService.cs:84-209` предпочитает удалённый grant, а при network/timeout/5xx использует проверенный cache. `LicenseDecisionService.cs:23-76` применяет срок и grace. WPF переводит окно в Service в `MainWindow.xaml.cs:2313-2355`, затем `ServiceOperationGuard.cs:21-40` допускает локальные операции на основании snapshot.
5. Администрирование: `AdminController` проверяет `X-Admin-Key` методом `IsAdmin()` в начале каждого просмотренного action (`AdminController.cs:857-865`); локальный пароль HonestDesk охраняет административные окна, но фоновая синхронизация использует ключ до разблокировки (`HonestDesk/MainWindow.xaml.cs:81-110,175-254`). Ролей и отдельных прав у единого API-ключа нет.

### Поверхность API и видимость данных

| Субъект | Что доступно по проверенному маршруту | Граница |
|---|---|---|
| Без токена / Free | Публичные текущие версии и метаданные обновления (`VersionController`, `PublicHonestFlowUpdateController`), создание заявки с сайта (`ConnectionRequestsController`), login/refresh и получение install-only доступа по отдельному паролю. В `Program.cs:224-230` Swagger и корневой redirect открыты без отдельной авторизации; схема раскрывает структуру API, но сама по себе не даёт доступа к данным. | Для защищённых маршрутов нужен bearer или admin key; лимит публичных заявок по IP не исключает распределённый спам. |
| Pending device | `DeviceController.registration/current` и `device/request` для собственной pending-сессии. | Нельзя читать конфигурацию, текущий grant и Service API до решения оператора; принадлежность физического ПК не доказывается (HF-SEC-002). |
| Active device без Service / с отозванным grant | Свои `configuration/current` и `license/current` (последний возвращает 404/410 при отсутствии/отзыве/истечении); конфигурация содержит чувствительные поля (HF-SEC-003). | `ServicePolicy` запрещает support и обычный asset download. |
| Active device с Service | Свои support-заявки, RuDesktop credential, диагностика, разрешённые component assets. | Claim `ClientId/DeviceId` выводится из серверной сессии; ссылки на другие устройства в этих маршрутах не являются источником полномочий. Presigned storage URL — отдельная временная capability (HF-SEC-013). |
| Истёкший/отозванный bearer; Disabled/Deleted device | Bearer handler и active-device policy прекращают выдачу защищённых данных; refresh зависит от состояния token/client/device. | Локальный HF может продолжать ранее разрешённые действия до перепроверки (HF-SEC-008). |
| HonestDesk с `X-Admin-Key` | Все операции `AdminController`: клиенты, устройства, заявки, лицензии, настройки интеграции, версии/assets, поддержка/диагностика, install-access. | Нет разделения read/write/publish по ролям (HF-SEC-004); владение ключом является полным административным допуском. |

Глобальный `UseExceptionHandler()` возвращает ProblemDetails вместо штатной выдачи stack trace; трассировки в ответах и журналы с реальными данными runtime не проверялись. В просмотренных пользовательских выборках лицензия и конфигурация берутся по claims текущей сессии, а не по произвольному `clientId` запроса. Это статический вывод, не доказательство отсутствия IDOR во всём приложении.

## B. Реестр проблем

| ID | Severity | Компонент | Уверенность | Кратко |
|---|---|---|---|---|
| HF-SEC-001 | High | HF | Confirmed | Другой локальный пользователь может прочитать machine-DPAPI сессию. |
| HF-SEC-002 | High | HF + Server | Confirmed | DeviceId — предъявляемая строка, а не доказательство принадлежности ПК. |
| HF-SEC-003 | High | Server | Confirmed | Идентификационный пароль и CHZ-токен выдаются Active Device без Service. |
| HF-SEC-004 | High | Desk | Confirmed | Пароль интерфейса не защищает API-ключ от процесса под тем же Windows-пользователем. |
| HF-SEC-005 | High | HF | Likely | Elevated update script в ProgramData может быть подготовлен/подменён менее привилегированным пользователем. |
| HF-SEC-006 | High | HF + Server | Confirmed | Обновляемый EXE и компоненты не имеют независимой проверки подписи издателя. |
| HF-SEC-007 | Medium | HF | Likely | Контроль часов и удаление clock-anchor позволяют повторять offline grace. |
| HF-SEC-008 | Medium | HF | Confirmed | Активный snapshot допускает локальные операции до следующей проверки (60–75 мин). |
| HF-SEC-009 | Medium | HF | Confirmed | WPF Service guard не проверяет отдельные feature-флаги операций. |
| HF-SEC-010 | Medium | HF + Server | Confirmed | Отсутствующая policy трактуется HF и сервером по-разному. |
| HF-SEC-011 | Medium | Server | Likely | Refresh rotation не обнаруживает replay; конкурентные refresh не сериализованы. |
| HF-SEC-012 | Medium | Server | Confirmed | Неуспешный install-access дорог по CPU и пишет AuditEvents. |
| HF-SEC-013 | Medium | Server + Desk | Confirmed | Presigned upload ограничен только заявленной метаинформацией, контроль object hash отсутствует. |
| HF-SEC-014 | Medium | Server + Desk | Confirmed | IP-лимиты и полные административные выгрузки плохо масштабируются. |
| HF-SEC-015 | Medium | HF | Confirmed | Downloader пишет ответ до EOF без отсечения по заявленному размеру. |

### HF-SEC-001 — machine-DPAPI сессия доступна пользователям этого ПК

| Поле | Содержание |
|---|---|
| ID | HF-SEC-001 |
| Severity | High |
| Component | HF |
| CWE | CWE-200, CWE-522 |
| Location | `Infrastructure/Api/FileApiSessionStore.cs:16-33,43-53`; `Infrastructure/Api/DpapiApiSessionProtector.cs:9-19`; также `Infrastructure/DeviceIdentity/DpapiDeviceIdentityStateProtector.cs:9-26`, `Infrastructure/Licensing/DpapiTrustedLicenseClock.cs:76-83`. |
| Root Cause | Сессия с access/refresh token записывается в `%ProgramData%/HonestFlow/api-session.dpapi` с `DataProtectionScope.LocalMachine`. Дополнительная entropy — строка из исходников, не секрет. Фактический ACL этого файла, `device-identity.dpapi` и `license-clock.dpapi` на обследованном ПК даёт встроенной группе Users чтение `RX`. |
| Attack Preconditions | Другой локальный Windows-пользователь может прочитать эти файлы; сессия должна быть сохранена. |
| Impact | Извлечение переносимых bearer/refresh token, DeviceId, последующее обращение к API как клиент; при действующей Service-лицензии — сервисные API до отзыва. Это не раскрывает закрытый ECDSA-ключ. |
| Evidence | [Документация Microsoft по `LocalMachine`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope): любой процесс на том же ПК может снять защиту при доступе к файлу. ACL прочитан через `icacls` без чтения содержимого. |
| Reproduction | В отдельной тестовой Windows с двумя обычными учётными записями записать **фиктивный** machine-DPAPI blob в каталог с тем же ACL и проверить снятие защиты второй записью; реальные токены не открывать. |
| Remediation | `CurrentUser` для токенов, явный ACL каталога/файла, короткие сессии и ротация при миграции. Для машинного многопользовательского сценария нужен отдельный service/account boundary, а не `LocalMachine` с общим чтением. |
| Confidence | Confirmed для исходников, semantics DPAPI и ACL конкретного ПК; наличие такого ACL на всех клиентских ПК не проверялось. |

### HF-SEC-002 — перенос DeviceId и действующей лицензии на другую машину

| Поле | Содержание |
|---|---|
| ID | HF-SEC-002 |
| Severity | High |
| Component | HF + Server |
| CWE | CWE-287, CWE-302 |
| Location | `Infrastructure/DeviceIdentity/FileDeviceIdentityService.cs:101-117,138-156`; `Infrastructure/Api/ApiSessionService.cs:51-60`; server `Controllers/AuthController.cs:27-75`, `Authentication/OpaqueBearerHandler.cs:38-52`. |
| Root Cause | DeviceId — случайный GUID установки. Login принимает произвольную строку `request.DeviceId`; сервер ищет совпадение в БД и не требует доказательства владения секретом устройства. Bearer/refresh-токены также не привязаны к ключу устройства или TLS-каналу. |
| Attack Preconditions | Известны пароль клиента и DeviceId либо уже извлечён refresh token; последний сценарий не требует пароля. |
| Impact | Модифицированный клиент на другом ПК может представить зарегистрированный DeviceId и получить сессию/подписанный grant для этой пары. Сервер не сможет отличить его от исходного ПК. |
| Evidence | `AuthController.cs:62-70` устанавливает `DeviceId` сессии по совпадению строки; `DeviceBindingGuard.cs:18-29` запрещает привязку *к другому клиенту*, но не подтверждает физическую машину. |
| Reproduction | На временной тестовой БД/локальном TestServer с фиктивным клиентом и устройством сравнить login из двух изолированных клиентов с одинаковым DeviceId; не использовать реальные учётные данные. |
| Remediation | Если запрет переносов является бизнес-требованием, ввести device-generated неэкспортируемый ключ и challenge/подпись при login/refresh; учесть TPM/переустановку и процедуру переноса. Не считать hardware fingerprint абсолютной защитой. |
| Confidence | Confirmed как архитектурная граница; практический перенос на отдельной машине не испытывался. |

### HF-SEC-003 — секреты в configuration без действующей Service-лицензии

| Поле | Содержание |
|---|---|
| ID | HF-SEC-003 |
| Severity | High |
| Component | Server |
| CWE | CWE-200, CWE-522 |
| Location | server `Controllers/ConfigurationController.cs:15-36,67-77`, `Controllers/AuthController.cs:34-39`, `Controllers/AdminController.cs:625-648`; `tests/.../ServiceEntitlementTests.cs:210-220`. |
| Root Cause | `/api/configuration/current` использует `ActiveDevicePolicy`, а не `ServicePolicy`, и возвращает `IdentificationCode` + `ChzToken`. `IdentificationCode` одновременно равен login-паролю: он хранится в `ClientSettings` в открытом виде, хотя в `Credentials` есть PBKDF2-хеш. |
| Attack Preconditions | Любая действующая сессия Active Device, включая устройство без Service-гранта/после отзыва гранта. Для получения первой сессии всё же нужен пароль или refresh token. |
| Impact | Секрет интеграции CHZ и повторно используемый идентификационный пароль раскрываются клиенту, которому уже отказано в Service. Кража SQLite-файла раскрывает эти поля без перебора PBKDF2. |
| Evidence | Контракт `ConfigurationResponse` формируется прямо из `settings?.IdentificationCode` и `settings?.ChzToken`; существующий тест специально фиксирует доступность configuration без Service. |
| Reproduction | Локальный TestServer: Active Device с отозванным Service-грантом; проверить, что `/api/configuration/current` возвращает секретные поля, а `/api/support/...` даёт 403. В отчёт выводить только факт наличия полей. |
| Remediation | Развести пароль входа и идентификационный код/токен, убрать пароль из конфигурационного ответа; CHZ выдавать по отдельной потребности и праву. Сохранить порядок HF startup, где configuration читается до license, через отдельный не-секретный bootstrap DTO. После миграции ротировать ранее выданные секреты. |
| Confidence | Confirmed по API и тесту; требования к доступности CHZ у клиента требуют продуктового решения. |

### HF-SEC-004 — пароль HonestDesk является только UI-ограничением

| Поле | Содержание |
|---|---|
| ID | HF-SEC-004 |
| Severity | High |
| Component | Desk |
| CWE | CWE-306, CWE-522 |
| Location | Desk `Services/AdminAccessService.cs:45-95`; `Models/AppSettings.cs:5-8`; `Services/DpapiProtector.cs:37-50`; `MainWindow.xaml.cs:81-110,175-254,651-662`. |
| Root Cause | Сервер знает только общий `X-Admin-Key`; ключ хранится в user-profile `settings.json` под DPAPI текущего пользователя. Фоновая синхронизация использует этот ключ **до** локальной разблокировки. `AdminAccessService.Verify()` не даёт ключу криптографически значимого допуска, а `_adminSessionUntilUtc` после unlock устанавливается в `DateTimeOffset.MaxValue`. |
| Attack Preconditions | Выполнение произвольного кода от той же Windows-учётной записи, что запускает Desk, либо её компрометация. Другой Windows-пользователь по умолчанию user-DPAPI blob не расшифрует. |
| Impact | Обход пароля HonestDesk и всех UI-ограничений, административные API-действия, публикация метаданных обновлений. Доступ к закрытому ECDSA-ключу зависит отдельно от ACL его файла и учётной записи. |
| Evidence | `CreateApiClient()` вызывает `DpapiProtector.Unprotect()` непосредственно; сервер `AdminController.cs:857-865` принимает единственный статический ключ. |
| Reproduction | В тестовом профиле/сервере с фиктивным API-ключом проверить, что фоновый GET проходит до ввода пароля, и что другой процесс той же записи может открыть собственный DPAPI blob. |
| Remediation | Разнести поддержку и опасные admin-права; для публикации/изменений требовать короткую серверную сессию после интерактивной аутентификации. Хранить высокопривилегированный ключ вне постоянно доступного профиля оператора или защищать его отдельным credential boundary. Добавить время жизни unlock. |
| Confidence | Confirmed для code path; не проверялись ACL и защита конкретного файла закрытого ключа. |

### HF-SEC-005 — local privilege escalation через скрипт самообновления

| Поле | Содержание |
|---|---|
| ID | HF-SEC-005 |
| Severity | High |
| Component | HF |
| CWE | CWE-377, CWE-732 |
| Location | HF `app.manifest:19`, `Infrastructure/Updates/SelfUpdateService.cs:119-141,167-222`, `Infrastructure/Configuration/AppPaths.cs:59,73-80`. |
| Root Cause | HF требует Administrator, но создаёт/запускает `apply_update.ps1` в `%ProgramData%/HonestFlow/update` без явного ACL или безопасного владения. На проверенном ПК родительский каталог позволяет Users создавать дочерние объекты; `update` отсутствовал. Поэтому менее привилегированный пользователь потенциально может заранее создать/контролировать каталог или файл, а затем изменить скрипт между записью и чтением PowerShell. |
| Attack Preconditions | Обычный локальный пользователь с доступом к общему ProgramData; администратор позднее запускает HF и доступно обновление. |
| Impact | Возможный запуск произвольного скрипта с elevated token. Контроль `SHA-256` EXE внутри скрипта не защищает сам скрипт. |
| Evidence | `File.WriteAllText(scriptPath, script)` и `Process.Start(powershell.exe -File scriptPath)`; локальные ACL проверены без создания файлов. |
| Reproduction | Только в одноразовой тестовой VM: создать фиктивный `update` как обычный пользователь, затем безопасный тестовый файл и измерить владельца/ACL и возможность изменения между двумя этапами. Production-каталог не трогать. |
| Remediation | Создать каталог/скрипт с explicit Administrators+SYSTEM ACL до записи, исключить reparse points, запускать временный скрипт из защищённого пути и сверять целостность перед выполнением; лучше заменить PowerShell на подписанный updater с безопасной передачей параметров. |
| Confidence | Likely: реальный TOCTOU/ACL наследования дочерних объектов не эксплуатировался. |

### HF-SEC-006 — SHA-256 не подтверждает издателя обновления

| Поле | Содержание |
|---|---|
| ID | HF-SEC-006 |
| Severity | High |
| Component | HF + Server |
| CWE | CWE-494, CWE-347 |
| Location | server `Controllers/AdminController.cs:396-404,498-537`; `Controllers/PublicHonestFlowUpdateController.cs:23-42`; HF `Infrastructure/Updates/SelfUpdateService.cs:62-95,190-205`; `Infrastructure/Downloads/VerifiedAssetDownloader.cs:79-89`. |
| Root Cause | EXE проверяется по размеру и SHA-256, но оба эталона публикует тот же сервер через admin-key путь. Отдельной проверки Authenticode/подписи издателя или immutable release-manifest в проверенной цепочке нет. Аналогично компонентные MSI/EXE получают hash из серверной configuration. |
| Attack Preconditions | Компрометация admin API-ключа/серверной БД/канала публикации, но не ECDSA-ключа лицензий. |
| Impact | Злоумышленник публикует свой файл и соответствующий hash, после чего HF принимает его как обновление и запускает elevated. Подпись лицензии не ограничивает публикацию бинарников. |
| Evidence | Метод `HasTrustedMetadata` проверяет лишь формат hash/size; `VerifiedAssetDownloader` сравнивает байты с предоставленным hash. `SelfUpdateService` запускает результат. |
| Reproduction | Изолированный TestServer с фиктивным ключом и безвредным тестовым EXE: сравнить принятие корректного hash для файла без подписи издателя; не запускать неизвестный код. |
| Remediation | Подписывать точные байты манифеста отдельным офлайн-ключом ECDSA и проверять его в HF перед заменой EXE и запуском компонентов; отделить права публикации от общего admin key и запретить downgrade. Платный Windows Authenticode пользователь исключил. Нужны ротация ключа и совместимый переход старых сборок. |
| Confidence | Confirmed для отсутствия независимой проверки в прослеженном пути. |

### HF-SEC-007 — сброс локального clock-anchor повторяет grace

| Поле | Содержание |
|---|---|
| ID | HF-SEC-007 |
| Severity | Medium |
| Component | HF |
| CWE | CWE-367, CWE-841 |
| Location | HF `Infrastructure/Licensing/DpapiTrustedLicenseClock.cs:26-47,76-83,104-125`; `Application/Licensing/LicenseDecisionService.cs:53-64,99-112`; `Infrastructure/Licensing/FileLicenseManifestCache.cs:203-243`. |
| Root Cause | При отсутствии `license-clock.dpapi` clock принимает текущие системные часы. Проверенный cache хранит фиксированное время последней online-проверки; его нельзя произвольно изменить без DPAPI, но можно оставить неизменным и после перезагрузки выставить часы немного позже этого времени. Такой `now` снова попадает в тот же grace-интервал. |
| Attack Preconditions | Контроль системных часов и локальных файлов ПК; действующий ранее подписанный и ещё не истёкший по подставленному времени grant/cache. |
| Impact | Повторение локального offline Service дольше фактического grace; серверные Service API остаются недоступны без актуальной серверной проверки. Возможна также ложная блокировка честного пользователя после скачка часов вперёд. |
| Evidence | Конструктор clock выбирает `storedUtc ?? systemUtc`; `TryValidateContext` отвергает только `LastOnline > now`; grace вычисляется как `LastOnline + OfflineGraceHours`. |
| Reproduction | Тестовая VM, фиктивный grant, отключённая сеть, snapshot VM: сравнить решения после контролируемого rollback часов с сохранённым cache и отсутствующим clock-файлом. Не изменять часы рабочего ПК. |
| Remediation | Честно считать offline grace ограничением для некомпрометированного клиента; для строгой защиты требовать серверный challenge/периодическое онлайн-подтверждение. Не ломать легальные offline-сценарии ради неосуществимой абсолютной защиты от владельца ПК. |
| Confidence | Likely до изолированной временной проверки; логический путь в коде установлен. |

### HF-SEC-008 — отзыв/истечение локально применяются с задержкой

| Поле | Содержание |
|---|---|
| ID | HF-SEC-008 |
| Severity | Medium |
| Component | HF |
| CWE | CWE-613, CWE-672 |
| Location | HF `Application/Licensing/LicenseRefreshWorkflow.cs:36-59`; `Application/Licensing/ServiceOperationGuard.cs:21-40`; `UI/MainWindow.xaml.cs:1038-1066,2342-2355`; server `Authentication/ServiceEntitlementRequirement.cs:33-80`. |
| Root Cause | Guard использует старое `runtime.Entitlement.Decision == Allowed`, не пересчитывая срок/grace в момент локальной операции. Следующий refresh назначается через случайные 60–75 минут. |
| Attack Preconditions | Service уже активен; администратор отзывает grant либо срок истекает сразу после успешной проверки. |
| Impact | Локальные установки/исправления потенциально доступны до очередной проверки. Серверные support/assets маршруты проверяют актуальное состояние и должны отказать сразу; это разные границы. |
| Evidence | В guard нет проверки `ValidUntilUtc`, `OfflineGraceEndsAtUtc` или clock; periodic delay явно задан. |
| Reproduction | Изолированный clock/test double: установить snapshot Allowed, перевести виртуальное время за срок, вызвать локальный `Demand`; отдельно проверить серверный 403 после отзыва. |
| Remediation | Хранить проверенный deadline в runtime и проверять его перед каждой опасной операцией; отзыв онлайн обновлять чаще/по push при необходимости, с backoff и джиттером. Мгновенный отзыв при отсутствии связи невозможен без отказа от offline-режима. |
| Confidence | Confirmed для локальной задержки; фактическое окно зависит от момента refresh. |

### HF-SEC-009 — Service подменяет granular feature-права

| Поле | Содержание |
|---|---|
| ID | HF-SEC-009 |
| Severity | Medium |
| Component | HF |
| CWE | CWE-863 |
| Location | HF `UI/MainWindow.xaml.cs:1448-1449`; `Application/Licensing/ServiceOperationGuard.cs:21-40`; `Application/Licensing/LicenseAccessPolicy.cs:77-99`; Desk `Infrastructure/Licensing/ServerDeviceLicenseService.cs:59-64,79-94`. |
| Root Cause | Desk умеет выпускать отдельные `ViewAndRepair`, `InstallAndMaintenance` и `Service`; старый `LicenseAccessPolicy` различает операции. Production WPF создаёт `ServiceOperationGuard`, который после одного `Service` разрешает любую `LicenseOperation`, включая `InstallComponents` и `AutoFix`. |
| Attack Preconditions | Устройство с подписанным Service-грантом, в котором один из отдельных feature-флагов выключен. |
| Impact | Локальная операция выполняется вопреки гранулярному назначению в подписанном grant. Серверный `ServicePolicy` также проверяет только `Service` и не накладывает ограничения на конкретный тип component asset. |
| Evidence | Прямое сравнение guard и grant builder; существующий `ServiceOperationGuardTests.cs:9-19` проверяет только отсутствие runtime, не ограничения features. |
| Reproduction | Unit test с Service snapshot и только `Service` в Features: вызвать `Demand(InstallComponents)` и сравнить с `LicenseAccessPolicy.Check`. |
| Remediation | Зафиксировать продуктовую семантику: либо Service сознательно включает все локальные функции и убрать вводящие в заблуждение отдельные флаги, либо требовать и Service, и feature конкретной операции. |
| Confidence | Confirmed для поведения кода; трактовка как уязвимости зависит от заявленного тарифного разграничения. |

### HF-SEC-010 — `LicensePolicies` отсутствует: HF может показать Service, сервер откажет

| Поле | Содержание |
|---|---|
| ID | HF-SEC-010 |
| Severity | Medium |
| Component | HF + Server |
| CWE | CWE-841 |
| Location | server `Authentication/ServiceEntitlementRequirement.cs:33-38`; `Controllers/AuthController.cs:46-50,73-75`; HF `Infrastructure/Api/ApiAuthService.cs:43-45,78-83`; `Application/Licensing/LicenseDecisionService.cs:36-41`. |
| Root Cause | Сервер требует `Any LicensePolicy(IsEnabled)`, поэтому отсутствующая строка означает deny. HF проверяет `LicensePolicyEnabled == false`, а `null` не блокирует и переводит решение на подписанный `grant.ClientEnabled`. |
| Attack Preconditions | Существующий подписанный Service-grant для Active Device, но нет строки `LicensePolicies` (например, импорт/неполная миграция). |
| Impact | WPF активирует Service и локальные операции, а защищённые server API дают `service_required`. Это также вводит пользователя в заблуждение при временных ошибках. |
| Evidence | Сравнение nullable token response и строгого server `AnyAsync`; состояние строки не проверялось в production. |
| Reproduction | Только временная SQLite в TestServer: grant + Active Device без policy; сравнить клиентский evaluator и `/api/support/...`. |
| Remediation | Единая политика `missing` на клиенте и сервере; миграция исторических клиентов, затем тесты на отсутствующую строку. |
| Confidence | Confirmed для условного code path; неизвестно, существуют ли такие записи в production. |

### HF-SEC-011 — refresh replay и конкурентная ротация

| Поле | Содержание |
|---|---|
| ID | HF-SEC-011 |
| Severity | Medium |
| Component | Server |
| CWE | CWE-613, CWE-362 |
| Location | server `Controllers/AuthController.cs:84-116,135-147`; `Models/DatabaseModels.cs:141-158`; `Data/HonestDbContext.cs:44-45`. |
| Root Cause | `Refresh` читает `RevokedAtUtc`, затем меняет его и создаёт новый token без явной немедленной транзакции/optimistic concurrency check. Повторное предъявление уже ротированного токена только даёт 401; `TokenFamilyId` не используется для отзыва дочерних токенов при replay. |
| Attack Preconditions | Похищен refresh token или идут параллельные refresh одной сессии. |
| Impact | Кто предъявил украденный токен первым, может сохранить собственную действительную ветку на срок refresh; легальный клиент получит 401 и потеряет сессию. Две конкурентные ветки возможны до подтверждения изолированным тестом. |
| Evidence | Две `SaveChangesAsync()` после предварительного чтения, отсутствие проверки `WHERE RevokedAtUtc IS NULL` при условном update и отсутствие обработки reuse по family. |
| Reproduction | Локальный TestServer/временная SQLite: два синхронизированных refresh одного фиктивного токена; отдельно предъявить предыдущее значение после успешной ротации и проверить состояние всей family. |
| Remediation | Атомарное compare-and-swap/transaction для ротации, повторное использование старого токена считать компрометацией и отзывать всю family; предусмотреть безопасную обработку сетевой потери ответа у честного клиента. |
| Confidence | Likely для гонки; отсутствие family-reuse detection подтверждено кодом. |

### HF-SEC-012 — install-access истощает CPU и растит журнал аудита

| Поле | Содержание |
|---|---|
| ID | HF-SEC-012 |
| Severity | Medium |
| Component | Server |
| CWE | CWE-400, CWE-307 |
| Location | server `Controllers/ServiceInstallationController.cs:23-59,89-104`; `Data/PasswordHasher.cs:7-26`; `Program.cs:195-203`; `Infrastructure/LoginAttemptLimiter.cs:9-29`. |
| Root Cause | Каждый неверный install-пароль проверяется PBKDF2 (210k итераций), затем пишет AuditEvent и `SaveChangesAsync`. Лимит 10/мин на IP, глобальной очереди/ограничения дорогих операций и retention AuditEvents нет. Login limiter также хранит хеш каждого отличающегося неверного пароля в неограниченном `ConcurrentDictionary`. |
| Attack Preconditions | Публичный доступ к API с одного либо многих IP; правильный пароль не нужен. |
| Impact | CPU + SQLite write contention/рост БД; при распределённых запросах лимит по отдельному IP недостаточен. Это анализ кода, не измеренная доступность. |
| Evidence | Прямой путь неверного пароля через PBKDF2 → AddAudit → SaveChanges; `LoginAttemptLimiter` не удаляет неуспешные ключи. |
| Reproduction | На локальном TestServer с жёстким потолком, например не более 2 RPS, 30 секунд, 2 параллельных запроса, измерить CPU/рост AuditEvents; никогда не направлять на production. |
| Remediation | Ограничить глобальную конкурентность дорогой проверки, ограничить и очищать limiter state, агрегировать/сэмплировать неуспешный аудит и задать retention; защитить API на edge. |
| Confidence | Confirmed для затрат/роста на запрос; порог деградации требует измерения. |

### HF-SEC-013 — presigned диагностика не ограничивает реальные байты

| Поле | Содержание |
|---|---|
| ID | HF-SEC-013 |
| Severity | Medium |
| Component | Server + Desk |
| CWE | CWE-400, CWE-345 |
| Location | server `Controllers/SupportController.cs:180-262`; `Contracts/SupportContracts.cs:13-27`; `Infrastructure/DiagnosticObjectStorage.cs:61-71,90-102`; Desk `MainWindow.xaml.cs:888-904`. |
| Root Cause | JSON заявляет максимум 500 MiB и SHA-256, но presigned S3 PUT не содержит фактического size-limit. `complete` получает только S3 `ContentLength`, не вычисляет SHA-256 объекта. URL действует до 5–60 минут и может допустить повторную запись ключа в пределах TTL. |
| Attack Preconditions | Действительная Service-сессия и доступ к выданному upload URL. |
| Impact | Расход облачного хранилища/трафика сверх заявленного размера до уборки, несоответствие метаданных и объекта, отказ оператора от скачивания из-за проверки SHA. Desk действительно сверяет SHA перед открытием, поэтому silent execution подменённого ZIP этим путём не доказан. |
| Evidence | S3 signer задаёт verb PUT и ContentType, а HEAD возвращает только `ContentLength`; cleanup обходит до 100 записей каждые 15 мин. |
| Reproduction | Локальный fake S3/TestServer: заявить маленький размер, отправить больший объект, проверить отказ `complete` и наличие объекта до cleanup; без реальных облачных ключей. |
| Remediation | Ограничить размер на storage policy/POST policy или прокси upload, завершать запись только после контроля хеша, выдавать одноразовый ключ/короткий TTL, удалять несоответствующий объект немедленно и вести квоты на клиента. |
| Confidence | Confirmed для отсутствующих проверок в собственном коде; дополнительные лимиты Cloud.ru bucket policy не изучались. |

### HF-SEC-014 — 10 000 клиентов: NAT rate limit и неограниченные списки

| Поле | Содержание |
|---|---|
| ID | HF-SEC-014 |
| Severity | Medium |
| Component | Server + Desk |
| CWE | CWE-400 |
| Location | server `Program.cs:152-203`; `Controllers/AdminController.cs:95-105,159-184,283-312,712-750`; Desk `MainWindow.xaml.cs:175-224`. |
| Root Cause | Login 10/мин/IP, refresh 30/мин/IP, включая клиентов за одним NAT. Desk каждые ~30 секунд запрашивает полные списки support, diagnostics, pending registration, devices и licenses, а сервер возвращает их без pagination/retention. |
| Attack Preconditions | Много легальных устройств за одним внешним IP либо большой объём истории; атака не обязательна. |
| Impact | Ложные 429 при массовом восстановлении связи; полные сканы/сериализация/передача растут вместе с историей. SQLite — одна write-очередь для login/refresh/audit/заявок. |
| Evidence | Жёсткие `PermitLimit` и `ToListAsync()` без `Take`/cursor; Desk объединяет полные списки в память. При 10 000 одновременных login с **одного** IP лимит позволяет лишь 10 в первую минуту — оценка по конфигурации, не замер. |
| Reproduction | Локальный TestServer и синтетическая временная БД: сначала 2–5 RPS, 60 секунд, максимум 10–20 виртуальных клиентов; измерить 429, p95, SQLite busy, размер ответов; масштабировать только после установки ресурсных лимитов. |
| Remediation | Разделить per-IP edge-лимит и per-account/device лимит, соблюдать NAT; добавить cursor/pagination, инкрементальные обновления и retention; смоделировать массовый reconnect с джиттером. |
| Confidence | Confirmed для логики и асимптотики, реальные p95/RPS и схема production-прокси не измерялись. |

### HF-SEC-015 — downloader не прерывает поток после заявленного размера

| Поле | Содержание |
|---|---|
| ID | HF-SEC-015 |
| Severity | Medium |
| Component | HF |
| CWE | CWE-400 |
| Location | HF `Infrastructure/Downloads/VerifiedAssetDownloader.cs:46-67`; `Infrastructure/Updates/SelfUpdateService.cs:76-84`; `Infrastructure/Api/ApiConfigurationInstallationPackageSource.cs:42-55`. |
| Root Cause | Цикл копирует байты до EOF. `SizeBytes` проверяется лишь после полного скачивания; `written > expected` не прерывает запись. Для self-update есть десятиминутный body timeout, но до него файл может занять большой объём. |
| Attack Preconditions | Некорректное/скомпрометированное хранилище или CDN, которым отвечает asset URL; объявленный hash сам по себе может оставаться корректным и запись всё равно продолжится до EOF. |
| Impact | Расход диска и трафика, возможный отказ обновления/установки. Итоговый файл не будет принят при неверном размере/hash, поэтому подмена исполняемого файла этим дефектом не доказана. |
| Evidence | `while (ReadAsync > 0) WriteAsync(...)` и поздний `IsVerified(partial, asset)`. |
| Reproduction | Локальный fake HTTP handler, объявить 1 KiB, вернуть несколько MiB; считать записанные байты в временном каталоге. |
| Remediation | Прекратить чтение при `written > SizeBytes`, ограничить общий размер и body deadline для всех типов asset; удалять partial при отказе. |
| Confidence | Confirmed по циклу. |

## C. Top 10 по реальному риску

1. **HF-SEC-001:** доступ к переносимым токенам из другого Windows-профиля на том же кассовом ПК.
2. **HF-SEC-005:** возможный запуск подменённого update-скрипта с правами администратора.
3. **HF-SEC-004:** компрометация пользователя Desk сразу даёт общий административный API-ключ.
4. **HF-SEC-006:** компрометация admin/publishing превращает обновление в доставку elevated EXE.
5. **HF-SEC-002:** пароль + DeviceId заменяют владение исходной машиной.
6. **HF-SEC-003:** конфигурация раскрывает повторно используемый пароль и CHZ-токен Active Device без Service.
7. **HF-SEC-012:** дешёвые для отправителя дорогие install-попытки пишут в SQLite.
8. **HF-SEC-014:** NAT-лимиты и полные списки мешают массовому восстановлению и росту.
9. **HF-SEC-007:** локальный grace можно продлить при контроле времени/файлов.
10. **HF-SEC-011:** похищенный refresh token не вызывает отзыв всей family после replay.

## D. Licensing Bypass Analysis

| Сценарий | Что реально возможно | Серверные полномочия |
|---|---|---|
| Один ПК, декомпиляция/патч | Изменить WPF/guard и вызвать локальные методы; без патча Service требует `LicenseDecision.Allowed` + `Service`. | Сервер всё равно проверяет текущую БД по `ServicePolicy`. |
| Перенос grant/cache на другой ПК | Подпись и subject проверяются, machine-DPAPI обычно не переносится как blob. При известных пароле + DeviceId можно получить **новую** сессию для зарегистрированного устройства из другого ПК (HF-SEC-002). | Да, пока client/device/license активны. |
| Offline grace | При обычном ходе времени ограничен подписанным `ValidUntilUtc` и last online check; с контролем clock/cache возможно повторение локального окна (HF-SEC-007). | Нет новых серверных прав. |
| Подмена ответа сервера | Неподписанный grant/неверный subject отбрасывается в HF. Клиентский patch может вообще убрать проверку локально. | Не создаёт валидную серверную сессию или grant. |
| Старый bearer/refresh token | Bearer отвергается после истечения/отзыва; refresh после rotation получает 401. Похищенный *действующий* refresh переносим; replay family не детектируется (HF-SEC-011). | Да, до обнаружения/отзыва/истечения. |
| Старая версия HF | Серверный `ServicePolicy` не доверяет клиентскому номеру версии; локальные слабые старые сборки могут использовать уже полученные данные. Конкретные старые бинарники не тестировались. | Зависит от текущих серверных правил; утверждать обход нельзя. |

Ни в HF, ни в сервере не найден закрытый ECDSA-ключ среди отслеживаемых Git-файлов по расширениям `pem/pfx/p12/key/db/sqlite`; это **не** проверка всех бинарных ресурсов и локальных секретных файлов. Публичный ключ в `EmbeddedLicenseTrust.cs` — ожидаемый trust anchor, а не секрет. Подписанный grant подтверждает права, тогда как JSON-конфигурации, DeviceId, UI-state и обычные checksum не являются криптографическим доказательством владения устройством или подписью издателя.

### Offline и переходы состояния

| Событие | Фактический путь | Вывод |
|---|---|---|
| DNS/API недоступен, timeout или 5xx при чтении grant | `ApiLicenseManifestRepository.cs:95-110` → `LicenseObservationService.cs:173-201` | Проверенный cache может дать grace; без cache — deny/Free. |
| 401/403/410 от license API | `MapStatus` → `CanUseCache` не включает эти статусы | Cache не используется, Service снимается при refresh. |
| 429 от license API | Также не входит в `CanUseCache` | Fail-closed, но честный клиент может потерять Service из-за rate limit. |
| Плохой JSON/подпись/subject | `ApiLicenseManifestRepository.cs:39-91` | Deny, а не fallback на cache. |
| Истёк access token | `ApiSessionService.cs:138-165` | Refresh перед запросом; 401/403 очищает локальную сессию. |
| Истёк refresh token + нет сети | `ApiAuthService.cs:68-107` | При сетевой ошибке использует защищённую cache-конфигурацию; затем cache grant с grace, если доступен. |
| Disabled/Deleted device | Сервер `AuthController.cs:62-68,99-105`; bearer claims из актуальной Device-записи | Login/refresh/service route запрещены. Локальный снимок обновится с задержкой HF-SEC-008. |
| Rejected request | `DeviceController.cs:82-116` допускает повторное открытие Rejected в Pending | Повторная заявка возможна; Approved нельзя подменить без admin key. |
| Удаление cache/clock | Cache отсутствует → отказ offline; отдельное удаление clock при сохранённом cache связано с HF-SEC-007 | Возможна и ложная блокировка, и clock rollback. |
| Перезагрузка Windows | Состояние сессии, grant cache и clock хранятся в ProgramData; `App.xaml.cs:18-47` открывает Free и запускает remembered Service resume | При сохранённых файлах и выполнении grace Service может вернуться; фактический runtime сценарий не прогонялся. |
| Переустановка приложения | `AppPaths.cs:59-67` хранит состояние вне каталога EXE; результат зависит от того, сохранил ли установщик/деинсталлятор ProgramData | Если файлы удалены, offline Service не восстановится; если сохранены, действует прежняя проверка подписи/cache/grace. Для конкретного установщика end-to-end не проверено. |
| Восстановление сети | `LicenseRefreshWorkflow.cs:36-69` переходит к удалённому чтению на следующем цикле; `LicenseObservationService` предпочитает серверный ответ | Отзыв/изменение policy будет применён после успешного refresh, с задержкой HF-SEC-008. Network/5xx могут перейти на grace cache; 429 вызывает deny, как указано выше. |

## E. Server Resilience Analysis

Наиболее дорогие пути по коду: `POST /api/service/install-access` (PBKDF2 + audit write), `POST /api/auth/login` (поиск credential, PBKDF2, immediate SQLite write, запись токена), `POST /api/auth/refresh` (несколько запросов и две записи), `/api/configuration/current` (несколько таблиц + все ComponentAssets), административные полные списки и диагностика (S3 HEAD/DELETE). Это **ранжирование по операциям**, не замер latency.

SQLite сериализует запись. Сервер выполняет auth/refresh, регистрацию, поддержку, аудит и метаданные диагностики в одной БД. В коде нет явно настроенных WAL/busy-timeout или политики retention для AuditEvents/RefreshTokens/SupportRequests; фактические PRAGMA production не читались. `Program.cs:206-215` выполняет schema ensure при старте, что тоже требует контролируемого окна deployment. При массовом reconnect случайный интервал periodic refresh 60–75 минут помогает steady state, но initial login после отключения интернета синхронно сходится в одну точку; IP-лимиты вызывают 429 за NAT. Следующий шаг — локальный профиль TestServer с временной БД, квотой 2–5 RPS/30–60 секунд/≤20 клиентов сначала, сбором p95, busy/locked, памяти и размера очередей, затем ступенчатое повышение только после ресурсов и stop conditions. Не выполнять этот сценарий на production.

## F. Remediation Roadmap

| Приоритет | Что сделать | Компоненты | Сложность | Побочные эффекты |
|---|---|---|---|---|
| P0 | Изолировать сохранённые токены: `CurrentUser`, строгие ACL, миграция/ротация старых сессий. | HF + Server | Средняя | Старый remembered login перестанет читаться другим профилем, потребуется повторный вход. |
| P0 | Защитить update staging/script и проверить издателя EXE/компонентов независимой подписью. | HF + Server/релизный процесс | Высокая | Новые ключи подписи, совместимость старых релизов, rollback-процедура. |
| P0 | Разделить API-ключ Desk по ролям, не держать опасные права в фоне до unlock. | Desk + Server | Высокая | Изменение API/админ-процесса и выпуска credentials. |
| P0 | Убрать идентификационный пароль и CHZ-токен из общего ActiveDevice configuration. | Server + HF | Высокая | Нужно перестроить startup/config contract и ротировать секреты. |
| P1 | Согласовать отсутствующую LicensePolicy и granular features во всех трёх частях. | HF + Server + Desk | Средняя | Возможны изменения уже выданных грантов/доступов. |
| P1 | Проверять локальный deadline перед каждой опасной операцией; отрегулировать refresh интервал с джиттером. | HF | Средняя | Более частые проверки увеличат API/RPS; offline grace требует точной семантики. |
| P1 | Атомарная refresh rotation + replay family revoke. | Server + HF | Средняя | Честные повторные запросы после потери ответа могут потребовать нового входа. |
| P1 | Квоты/ограничение presigned upload, проверка фактического hash, уборка pending объектов. | Server + Cloud.ru + Desk | Средняя | Дополнительные S3 операции и задержка подтверждения. |
| P1 | Глобальный бюджет CPU для PBKDF2, capped limiter state/audit retention, pagination и NAT-friendly лимиты. | Server + Desk | Средняя/высокая | Потребуются новые API параметры и обновление Desk. |
| P2 | Ограничить скачивание после `SizeBytes`, общий body timeout и disk quota. | HF | Низкая | Некорректные CDN-ответы будут отбрасываться раньше. |
| P2 | Уточнить offline threat model и поддержку переноса устройств. | HF + Server + Desk | Высокая при device key | TPM и переустановка усложняют поддержку клиентов. |
| P3 | Отдельная dependency/CVE и binary-signature инвентаризация сборок. | Все | Средняя | Потребуется доверенный SBOM/release pipeline. |

## G. Verification Matrix

`PASS` означает подтверждённую проверкой работу. Здесь runtime-тесты не запускались, поэтому свойства, установленные только чтением кода, помечены `INCONCLUSIVE (static)`; конкретные обнаруженные нарушения помечены `FAIL`.

| Проверка | Результат | Основание / недостающее |
|---|---|---|
| Free startup без identity/license | INCONCLUSIVE | `App.xaml.cs` → `LocalApplicationBootstrap` не создаёт DeviceId. Runtime не запускался. |
| Подпись персонального grant до выдачи | INCONCLUSIVE | `AdminController.PublishLicense` проверяет подпись; `LicenseController` фильтрует `SignatureScope/SignatureVerifiedAtUtc`. |
| Server ServicePolicy: status, subject, срок, Service | INCONCLUSIVE | `ServiceEntitlementRequirement.cs:25-80`; существующие integration tests просмотрены, не запускались. |
| Broken access control на каждом admin action | INCONCLUSIVE | Все public async action `AdminController` содержат `IsAdmin()` в начале; иных контроллеров с admin key не обнаружено. Не подтверждает безопасность самого ключа. |
| Cross-client DeviceId ownership | INCONCLUSIVE | `DeviceBindingGuard` и транзакции login/request/approve; не является доказательством владения машиной. |
| SQL injection в просмотренных endpoint | INCONCLUSIVE | Бизнес-запросы построены EF LINQ; динамические schema SQL используют внутренние identifier helpers, пользовательские строки в них не проходят. Полный fuzz не выполнялся. |
| Секреты ActiveDevice без Service | FAIL | HF-SEC-003. |
| Разделение ролей HonestDesk API | FAIL | HF-SEC-004: один ключ для чтения/выдачи/обновлений. |
| Device proof-of-possession | FAIL | HF-SEC-002. |
| Offline grace против локального clock rollback | INCONCLUSIVE | HF-SEC-007, нужен тест VM. |
| Мгновенный локальный отзыв/истечение | FAIL | HF-SEC-008. |
| Гранулярные feature-флаги в WPF | FAIL | HF-SEC-009. |
| Refresh replay/race | INCONCLUSIVE | Нет изолированного конкурентного теста; family reuse handling отсутствует. |
| Production ACL всех рабочих мест | NOT TESTED | ACL наблюдались лишь на одной локальной Windows. |
| Runtime тесты API/клиента, нагрузка, 10k reconnect | NOT TESTED | Read-only режим; production не трогали. |
| Сетевой MITM, certificate pinning, TLS конфигурация Caddy | NOT TESTED | Из кода HF виден HTTPS endpoint; реальная TLS конфигурация не запрашивалась. |
| Уязвимости NuGet/SDK, подпись сторонних MSI | NOT TESTED | Требуют отдельной инвентаризации релизных артефактов и актуальной базы CVE. |
| Реальное отсутствие секретов в бинарных сборках/на диске | NOT TESTED | Реальные секретные файлы и память процессов не открывались. |

### Пределы вывода

Не найденный в просмотренном коде путь обхода не равен отсутствию уязвимости. Настройки Caddy, systemd, S3 bucket policy, файловые ACL других ПК, фактический размер/содержимое production-БД, подписанность релизных EXE/MSI и поведение старых версий не проверялись. Отчёт не содержит секретов, приватных ключей, содержимого локальных сессий или персональных данных.
