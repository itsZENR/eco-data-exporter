# Eco Market Dumper

[Русский](#русский) | [English](#english)

## Русский

Экспериментальный клиентский экспортёр данных Eco в JSON. Репозиторий хранит снимок исходников неактивного проекта. Разработка остановлена; совместимость с актуальными версиями Eco не проверялась.

Версия плагина: **0.2.5**. Исходный пакет: **v4.5**.

### Возможности

- **F8:** экспорт загруженной таблицы рынка Economy Viewer, включая цены, остатки, валюты и сведения о магазинах.
- **F9:** экспорт рецептов, навыков, связанных предметов, талантов и диагностических данных о модификаторах.
- Сохранение JSON с отметкой времени и копии последней выгрузки в папки `MarketDumps/` и `EcoDataDumps/` внутри каталога игры.

Это экспортёр данных. Калькулятор цен и торговый бот в комплект не входят. Выгрузки могут содержать имена игроков, состояние навыков и сведения о магазинах; реальные выгрузки игроков и серверов в репозитории отсутствуют.

### Исходное окружение

- Eco **0.14.1.1**
- **.NET 6**
- **BepInEx 6 IL2CPP** и сгенерированные interop-сборки

Исходная реализация и инструкции сохранены в файлах `Plugin.cs`, `EcoMarketDumper.csproj` и `README.txt`. Их контрольные суммы SHA-256 записаны в `SOURCE-MANIFEST.json`.

### Сборка

Потребуется установленная лицензионная копия игры с необходимыми сборками Eco, Unity, BepInEx и Il2CppInterop. Эти зависимости не распространяются в составе репозитория.

Укажите путь к установке в свойстве MSBuild `GameDir`. Пример для PowerShell:

```powershell
dotnet build -c Release '-p:GameDir=D:\SteamLibrary\steamapps\common\Eco'
```

После успешной сборки исходный проект автоматически копирует плагин в `BepInEx/plugins/EcoMarketDumper` внутри `GameDir`. После замены плагина перезапустите Eco: BepInEx не поддерживает его горячую перезагрузку.

При подготовке этого архива исходников сборка и проверка совместимости в игре не выполнялись. Изменения исходной версии v4.5 и контекст экспериментов описаны в `README.txt`.

### Состав архива

Три исходных файла кода и документации сохранены без изменений. Результаты локальной сборки в `bin/` и `obj/` исключены из Git и сохранены в отдельной полной локальной резервной копии. Предыдущей истории Git в исходной папке не было.

Это неофициальный проект. Он не означает одобрения или разрешения со стороны Strange Loop Games. Публикация исходников не подтверждает допустимость этой интеграции на конкретном сервере. Репозиторий не предоставляет лицензию на сторонние материалы игры.

---

## English

An experimental client-side JSON exporter for Eco, preserved as an inactive source snapshot. Active development has stopped, and compatibility with current Eco releases has not been verified.

Plugin version: **0.2.5**. Original source package: **v4.5**.

### Features

- **F8:** export the currently loaded Economy Viewer market, including prices, stock, currencies and store metadata.
- **F9:** export recipes, skills, referenced items, talents and diagnostic modifier information.
- Write timestamped JSON files and a latest copy to `MarketDumps/` and `EcoDataDumps/` under the game directory.

This is a data exporter. It does not include a pricing calculator or trading bot. Exports can contain player names, skill state and store information; no real player or server exports are included here.

### Original target environment

- Eco **0.14.1.1**
- **.NET 6**
- **BepInEx 6 IL2CPP** and its generated interop assemblies

The original implementation and instructions are preserved in `Plugin.cs`, `EcoMarketDumper.csproj` and `README.txt`. `SOURCE-MANIFEST.json` records their SHA-256 hashes.

### Building

Use an existing licensed game installation containing the required Eco, Unity, BepInEx and Il2CppInterop assemblies. These dependencies are not redistributed in this repository.

Set the MSBuild `GameDir` property to that installation. For example, in PowerShell:

```powershell
dotnet build -c Release '-p:GameDir=D:\SteamLibrary\steamapps\common\Eco'
```

The original project automatically copies the compiled plugin into `BepInEx/plugins/EcoMarketDumper` under `GameDir` after a successful build. Restart Eco after replacing the plugin; BepInEx does not hot-reload it.

No build or in-game compatibility test was performed while preparing this source archive. See `README.txt` for the original v4.5 changes and known research context.

### Archive scope

The three original source/documentation files are preserved without changes. Local build outputs in `bin/` and `obj/` are excluded from Git and retained in a separate complete local backup. No earlier Git history was present in the source directory.

This is an unofficial project and is not an endorsement or permission from Strange Loop Games. Publication of the source does not establish that this integration is permitted on a particular server. No license for third-party game content is granted by this repository.
