# Подпись выпусков HonestFlow

Отдельный ключ ECDSA P-256 подписывает манифест обновления. Это подпись данных HonestFlow, а не Windows Authenticode. Ключ лицензий HonestDesk для выпусков не используется.

## Формат

Утилита `tools/HonestFlow.ReleaseSigning` формирует JSON-манифест с `component`, `version`, `fileName`, `architecture`, `sizeBytes`, `sha256`. Точные UTF-8 байты этого JSON подписываются ECDSA P-256/SHA-256, подпись кодируется как DER. Выходной JSON содержит `keyId`, `manifestBase64`, `signatureBase64`. Клиент проверяет подпись **до** чтения полей и затем сравнивает скачанный файл с подписанными размером и SHA-256. URL загрузки в подпись не включён: он может меняться при переносе хранилища без изменения содержимого выпуска.

## Ключ

Команда `keygen` создаёт новый ключ и сохраняет закрытую часть только в зашифрованном файле `*.hfrelkey`. Формат файла `HFRK1`: случайные соль и nonce, PBKDF2-HMAC-SHA256 (250 000 итераций), AES-256-GCM. Пароль вводится в интерактивной консоли и не передаётся аргументом процесса. Публичный ключ SubjectPublicKeyInfo и `keyId` выводятся в отдельный `*.public.json`. Утилита откажется создавать/читать закрытый ключ внутри Git-репозитория.

Пример **для будущего выпуска**, после настройки серверного контракта и контролируемого перехода старых клиентов:

```powershell
dotnet run --project C:\Work\HonestFlow\tools\HonestFlow.ReleaseSigning\HonestFlow.ReleaseSigning.csproj -- keygen "$env:LOCALAPPDATA\HonestFlowReleaseSigning\release.hfrelkey"

dotnet run --project C:\Work\HonestFlow\tools\HonestFlow.ReleaseSigning\HonestFlow.ReleaseSigning.csproj -- sign "$env:LOCALAPPDATA\HonestFlowReleaseSigning\release.hfrelkey" HonestFlow "C:\Path\To\HonestFlow.exe" 3.1.0 x64 "C:\Path\To\honestflow-3.1.0-x64.signed.json"
```

После настройки публичного ключа на сервере команда `publish` выполняет весь
путь: подписывает файл, получает presigned PUT, загружает объект прямо в
Cloud.ru и просит сервер повторно проверить размер и SHA-256. Ключ API и пароль
закрытого ключа вводятся скрыто и не передаются аргументами процесса:

```powershell
dotnet run --project C:\Work\HonestFlow\tools\HonestFlow.ReleaseSigning\HonestFlow.ReleaseSigning.csproj -- publish "$env:LOCALAPPDATA\HonestFlowReleaseSigning\release.hfrelkey" HonestFlow "C:\Path\To\HonestFlow.exe" 3.1.0 x64 https://api.honestflow.ru --set-current
```

Без `--set-current` файл добавляется в каталог, но текущая версия компонента не
меняется. Той же командой публикуются `LmModule`, `AtolDriver`, `ESM` и
`Controller`; для них указывается соответствующий MSI/EXE.

`sign` сверяет заявленную версию с `FileVersion` EXE. Зашифрованный ключ и пароль нужны для каждого следующего выпуска; их резервные копии следует хранить раздельно. Не помещать закрытый ключ или пароль в Git, API, HonestFlow, HonestDesk, публикацию сервера либо логи.

Первый клиент с обязательной проверкой должен поступить через контролируемый установщик; старая версия не способна самостоятельно подтвердить новый манифест. Компонентные установщики требуют той же проверки перед запуском. Ротация ключа требует заранее встроенного второго публичного ключа.

Текущий production public key имеет ID `release_6bd64214975e48a3af2c20b8` и встроен
в `ReleaseTrustStore`. Закрытый ключ остаётся только в зашифрованном локальном
файле пользователя. Строгий режим клиента включён; перед выпуском этой сборки
сервер должен получить тот же public key, а текущий HF должен быть опубликован
с подписанным манифестом.
