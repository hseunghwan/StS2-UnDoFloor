using System.Collections.Generic;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Map;

namespace StS2UnDoFloor;

/// <summary>
/// The mod's own user-facing text, in the languages the game ships with.
/// <para>
/// The language follows the game's setting (<see cref="LocManager.Language"/>, a three-letter code such as
/// <c>eng</c> or <c>kor</c>); anything the mod has no entry for falls back to English. Words the game already
/// translates — the act number, the room type, "Cancel" — are read from the game's own loc tables through
/// <see cref="GameText"/> so they always match the rest of the UI, with a hard-coded fallback in case a game
/// update moves the key.
/// </para>
/// <para>
/// Translations are filled per language below. A new language only needs a dictionary added to
/// <see cref="ByLanguage"/>; a missing key silently uses the English one.
/// </para>
/// </summary>
public static class ModText
{
    // Keys. Every value is a composition format: {0}/{1} are filled in by the helpers at the bottom of this file.
    private const string KeyPreviousAct = "previousAct";
    private const string KeyNextAct = "nextAct";
    private const string KeyPastAct = "pastAct";
    private const string KeyFloor = "floor";
    private const string KeyRewindBody = "rewindBody";
    private const string KeyJumpBody = "jumpBody";
    private const string KeyTravelHere = "travelHere";
    private const string KeyRedoFloor = "redoFloor";
    private const string KeyKeepResult = "keepResult";
    private const string KeyCancel = "cancel";

    private static readonly Dictionary<string, string> English = new Dictionary<string, string>
    {
        [KeyPreviousAct] = "Previous act",
        [KeyNextAct] = "Next act",
        [KeyPastAct] = "{0} (past)",
        [KeyFloor] = "Floor {0}",
        [KeyRewindBody] = "Go back to {0}, {1}?\nLater floors stay available as another timeline.",
        [KeyJumpBody] = "Jump to {0}, {1} of an earlier timeline?\nYour current path stays available too.",
        [KeyTravelHere] = "Travel here",
        [KeyRedoFloor] = "Redo this floor",
        [KeyKeepResult] = "Keep result, re-pick path",
        [KeyCancel] = "Cancel"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> ByLanguage =
        new Dictionary<string, Dictionary<string, string>>
        {
            ["eng"] = English,

            ["zhs"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "上一阶段",
                [KeyNextAct] = "下一阶段",
                [KeyPastAct] = "{0}（过去）",
                [KeyFloor] = "第{0}层",
                [KeyRewindBody] = "返回{0} {1}？\n之后的楼层会作为另一条时间线保留。",
                [KeyJumpBody] = "跳转到旧时间线的{0} {1}？\n当前路线同样会保留。",
                [KeyTravelHere] = "前往此处",
                [KeyRedoFloor] = "重玩此层",
                [KeyKeepResult] = "保留结果，重选路线",
                [KeyCancel] = "取消"
            },

            ["zht"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "上一幕",
                [KeyNextAct] = "下一幕",
                [KeyPastAct] = "{0}（過去）",
                [KeyFloor] = "第 {0} 層",
                [KeyRewindBody] = "返回{0} {1}？\n之後的樓層會保留為另一條時間線。",
                [KeyJumpBody] = "跳至舊時間線的{0} {1}？\n目前的路線同樣會保留。",
                [KeyTravelHere] = "前往此處",
                [KeyRedoFloor] = "重玩此層",
                [KeyKeepResult] = "保留結果，重選路線",
                [KeyCancel] = "取消"
            },

            ["deu"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Vorheriger Akt",
                [KeyNextAct] = "Nächster Akt",
                [KeyPastAct] = "{0} (Vergangenheit)",
                [KeyFloor] = "Ebene {0}",
                [KeyRewindBody] = "Zurück zu {0}, {1}?\nSpätere Ebenen bleiben als andere Zeitlinie erhalten.",
                [KeyJumpBody] = "Zu {0}, {1} einer früheren Zeitlinie springen?\nDein aktueller Pfad bleibt ebenfalls erhalten.",
                [KeyTravelHere] = "Hierher reisen",
                [KeyRedoFloor] = "Diese Ebene wiederholen",
                [KeyKeepResult] = "Ergebnis behalten, Pfad neu wählen",
                [KeyCancel] = "Abbrechen"
            },

            ["esp"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Acto anterior",
                [KeyNextAct] = "Acto siguiente",
                [KeyPastAct] = "{0} (pasado)",
                [KeyFloor] = "Piso {0}",
                [KeyRewindBody] = "¿Volver a {0}, {1}?\nLos pisos posteriores se conservan como otra línea temporal.",
                [KeyJumpBody] = "¿Saltar a {0}, {1} de una línea temporal anterior?\nTu ruta actual también se conserva.",
                [KeyTravelHere] = "Viajar aquí",
                [KeyRedoFloor] = "Repetir este piso",
                [KeyKeepResult] = "Conservar el resultado y elegir otra ruta",
                [KeyCancel] = "Cancelar"
            },

            ["fra"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Acte précédent",
                [KeyNextAct] = "Acte suivant",
                [KeyPastAct] = "{0} (passé)",
                [KeyFloor] = "Étage {0}",
                [KeyRewindBody] = "Revenir à {0}, {1} ?\nLes étages suivants restent accessibles comme autre ligne temporelle.",
                [KeyJumpBody] = "Aller à {0}, {1} d'une ligne temporelle précédente ?\nVotre chemin actuel reste également disponible.",
                [KeyTravelHere] = "Voyager ici",
                [KeyRedoFloor] = "Refaire cet étage",
                [KeyKeepResult] = "Garder le résultat, rechoisir le chemin",
                [KeyCancel] = "Annuler"
            },

            ["ind"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Babak sebelumnya",
                [KeyNextAct] = "Babak berikutnya",
                [KeyPastAct] = "{0} (lampau)",
                [KeyFloor] = "Lantai {0}",
                [KeyRewindBody] = "Kembali ke {0}, {1}?\nLantai setelahnya tetap tersedia sebagai garis waktu lain.",
                [KeyJumpBody] = "Lompat ke {0}, {1} dari garis waktu sebelumnya?\nJalur Anda saat ini juga tetap tersedia.",
                [KeyTravelHere] = "Pergi ke sini",
                [KeyRedoFloor] = "Ulangi lantai ini",
                [KeyKeepResult] = "Simpan hasil, pilih jalur lagi",
                [KeyCancel] = "Batalkan"
            },

            ["ita"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Atto precedente",
                [KeyNextAct] = "Atto successivo",
                [KeyPastAct] = "{0} (passato)",
                [KeyFloor] = "Piano {0}",
                [KeyRewindBody] = "Tornare a {0}, {1}?\nI piani successivi restano disponibili come un'altra linea temporale.",
                [KeyJumpBody] = "Saltare a {0}, {1} di una linea temporale precedente?\nAnche il percorso attuale resta disponibile.",
                [KeyTravelHere] = "Viaggia qui",
                [KeyRedoFloor] = "Rigioca questo piano",
                [KeyKeepResult] = "Mantieni il risultato, riscegli il percorso",
                [KeyCancel] = "Annulla"
            },

            ["jpn"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "前の層",
                [KeyNextAct] = "次の層",
                [KeyPastAct] = "{0}（過去）",
                [KeyFloor] = "{0}階",
                [KeyRewindBody] = "{0} {1}に戻りますか？\nそれ以降の階は別の時間軸として残ります。",
                [KeyJumpBody] = "以前の時間軸の{0} {1}に移動しますか？\n現在の経路もそのまま残ります。",
                [KeyTravelHere] = "ここへ進む",
                [KeyRedoFloor] = "この階をやり直す",
                [KeyKeepResult] = "結果を保持して分岐を選び直す",
                [KeyCancel] = "キャンセル"
            },

            ["kor"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "이전 막",
                [KeyNextAct] = "다음 막",
                [KeyPastAct] = "{0} (과거)",
                [KeyFloor] = "{0}층",
                [KeyRewindBody] = "{0} {1} 시점으로 돌아갈까요?\n이후 층은 다른 시간선으로 그대로 남습니다.",
                [KeyJumpBody] = "이전 시간선의 {0} {1} 시점으로 이동할까요?\n현재 경로도 그대로 남습니다.",
                [KeyTravelHere] = "여기로 이동",
                [KeyRedoFloor] = "이 층 다시 하기",
                [KeyKeepResult] = "결과 유지, 경로 다시 선택",
                [KeyCancel] = "취소"
            },

            ["pol"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Poprzedni akt",
                [KeyNextAct] = "Następny akt",
                [KeyPastAct] = "{0} (przeszłość)",
                [KeyFloor] = "Piętro {0}",
                [KeyRewindBody] = "Wrócić do: {0}, {1}?\nPóźniejsze piętra pozostaną jako inna linia czasu.",
                [KeyJumpBody] = "Przejść do: {0}, {1} z wcześniejszej linii czasu?\nObecna ścieżka również pozostanie dostępna.",
                [KeyTravelHere] = "Podróżuj tutaj",
                [KeyRedoFloor] = "Powtórz to piętro",
                [KeyKeepResult] = "Zachowaj wynik, wybierz ścieżkę ponownie",
                [KeyCancel] = "Anuluj"
            },

            ["ptb"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Ato anterior",
                [KeyNextAct] = "Próximo ato",
                [KeyPastAct] = "{0} (passado)",
                [KeyFloor] = "Andar {0}",
                [KeyRewindBody] = "Voltar para {0}, {1}?\nOs andares seguintes continuam disponíveis como outra linha do tempo.",
                [KeyJumpBody] = "Ir para {0}, {1} de uma linha do tempo anterior?\nSeu caminho atual também continua disponível.",
                [KeyTravelHere] = "Viajar para cá",
                [KeyRedoFloor] = "Refazer este andar",
                [KeyKeepResult] = "Manter o resultado e escolher outro caminho",
                [KeyCancel] = "Cancelar"
            },

            ["rus"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Предыдущий акт",
                [KeyNextAct] = "Следующий акт",
                [KeyPastAct] = "{0} (прошлое)",
                [KeyFloor] = "Этаж {0}",
                [KeyRewindBody] = "Вернуться к {0}, {1}?\nПоследующие этажи останутся как другая временная линия.",
                [KeyJumpBody] = "Перейти к {0}, {1} из прежней временной линии?\nТекущий путь тоже останется доступен.",
                [KeyTravelHere] = "Отправиться сюда",
                [KeyRedoFloor] = "Пройти этот этаж заново",
                [KeyKeepResult] = "Сохранить результат, выбрать путь заново",
                [KeyCancel] = "Отменить"
            },

            ["spa"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Acto anterior",
                [KeyNextAct] = "Acto siguiente",
                [KeyPastAct] = "{0} (pasado)",
                [KeyFloor] = "Piso {0}",
                [KeyRewindBody] = "¿Volver a {0}, {1}?\nLos pisos posteriores se conservan como otra línea temporal.",
                [KeyJumpBody] = "¿Saltar a {0}, {1} de una línea temporal anterior?\nTu ruta actual también se conserva.",
                [KeyTravelHere] = "Viajar aquí",
                [KeyRedoFloor] = "Repetir este piso",
                [KeyKeepResult] = "Conservar el resultado y elegir otra ruta",
                [KeyCancel] = "Cancelar"
            },

            ["tha"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "องก์ก่อนหน้า",
                [KeyNextAct] = "องก์ถัดไป",
                [KeyPastAct] = "{0} (อดีต)",
                [KeyFloor] = "ชั้น {0}",
                [KeyRewindBody] = "ย้อนกลับไปที่ {0} {1} หรือไม่?\nชั้นถัดจากนั้นจะยังคงอยู่เป็นอีกเส้นเวลาหนึ่ง",
                [KeyJumpBody] = "ข้ามไปที่ {0} {1} ของเส้นเวลาก่อนหน้าหรือไม่?\nเส้นทางปัจจุบันของคุณจะยังคงอยู่เช่นกัน",
                [KeyTravelHere] = "เดินทางมาที่นี่",
                [KeyRedoFloor] = "เล่นชั้นนี้ใหม่",
                [KeyKeepResult] = "เก็บผลลัพธ์ไว้ แล้วเลือกเส้นทางใหม่",
                [KeyCancel] = "ยกเลิก"
            },

            ["tur"] = new Dictionary<string, string>
            {
                [KeyPreviousAct] = "Önceki sahne",
                [KeyNextAct] = "Sonraki sahne",
                [KeyPastAct] = "{0} (geçmiş)",
                [KeyFloor] = "{0}. Kat",
                [KeyRewindBody] = "{0}, {1} konumuna dönülsün mü?\nSonraki katlar başka bir zaman çizgisi olarak kalır.",
                [KeyJumpBody] = "Önceki bir zaman çizgisindeki {0}, {1} konumuna geçilsin mi?\nMevcut yolun da kullanılabilir kalır.",
                [KeyTravelHere] = "Buraya git",
                [KeyRedoFloor] = "Bu katı tekrar oyna",
                [KeyKeepResult] = "Sonucu koru, yolu yeniden seç",
                [KeyCancel] = "İptal Et"
            }
        };

    /// <summary>The game's current language code, or <c>eng</c> before the LocManager exists.</summary>
    public static string Language
    {
        get
        {
            LocManager? manager = LocManager.Instance;
            return string.IsNullOrEmpty(manager?.Language) ? "eng" : manager!.Language;
        }
    }

    private static string Get(string key)
    {
        if (ByLanguage.TryGetValue(Language, out Dictionary<string, string>? table) &&
            table.TryGetValue(key, out string? text))
        {
            return text;
        }
        return English[key];
    }

    /// <summary>"&lt; Previous act", the button that steps back to the previous act with checkpoints.</summary>
    public static string PreviousActButton => "<  " + Get(KeyPreviousAct);

    /// <summary>"Next act &gt;", the button that steps forward towards the act being played.</summary>
    public static string NextActButton => Get(KeyNextAct) + "  >";

    /// <summary>Header above the act buttons: the act shown right now.</summary>
    public static string ActHeader(int actNumber, bool isPast)
    {
        string act = GameText.Act(actNumber);
        return isPast ? string.Format(Get(KeyPastAct), act) : act;
    }

    /// <summary>Dialog title: act, floor and room type of the clicked node.</summary>
    public static string DialogTitle(int actNumber, int floor, MapPointType pointType) =>
        $"{GameText.Act(actNumber)} - {Floor(floor)} - {GameText.RoomName(pointType)}";

    /// <summary>Dialog body when the node is on the path currently being played.</summary>
    public static string RewindBody(int actNumber, int floor) =>
        string.Format(Get(KeyRewindBody), GameText.Act(actNumber), Floor(floor));

    /// <summary>Dialog body when the node belongs to a timeline that was left behind.</summary>
    public static string JumpBody(int actNumber, int floor) =>
        string.Format(Get(KeyJumpBody), GameText.Act(actNumber), Floor(floor));

    public static string Cancel => GameText.Cancel ?? Get(KeyCancel);

    public static string TravelHere => Get(KeyTravelHere);

    public static string RedoFloor => Get(KeyRedoFloor);

    public static string KeepResult => Get(KeyKeepResult);

    private static string Floor(int floor) => string.Format(Get(KeyFloor), floor);
}

/// <summary>
/// Words the game already translates, read from its own loc tables. Every lookup is guarded: a game update that
/// renames a key must not take the mod's UI down with it, so the caller gets null (or the raw enum name) instead.
/// </summary>
internal static class GameText
{
    /// <summary>"Act 3" in the game's wording, which is not always "&lt;word&gt; &lt;number&gt;".</summary>
    internal static string Act(int actNumber)
    {
        LocString? loc = Lookup("gameplay_ui", "ACT_NUMBER");
        if (loc == null)
        {
            return $"Act {actNumber}";
        }
        loc.Add("actNumber", actNumber);
        return Format(loc) ?? $"Act {actNumber}";
    }

    /// <summary>The room type's display name, as the map legend and history tooltips show it.</summary>
    internal static string RoomName(MapPointType pointType)
    {
        string? key = pointType switch
        {
            MapPointType.Shop => "ROOM_MERCHANT",
            MapPointType.Treasure => "ROOM_TREASURE",
            MapPointType.RestSite => "ROOM_REST",
            MapPointType.Monster => "ROOM_ENEMY",
            MapPointType.Elite => "ROOM_ELITE",
            MapPointType.Boss => "ROOM_BOSS",
            MapPointType.Ancient => "ROOM_ANCIENT",
            _ => null
        };
        if (key == null)
        {
            return pointType.ToString();
        }
        LocString? loc = Lookup("static_hover_tips", key + ".title");
        return (loc == null ? null : Format(loc)) ?? pointType.ToString();
    }

    /// <summary>The game's "Cancel", or null when the key is gone.</summary>
    internal static string? Cancel
    {
        get
        {
            LocString? loc = Lookup("main_menu_ui", "MODDING_POPUP.cancel");
            return loc == null ? null : Format(loc);
        }
    }

    private static LocString? Lookup(string table, string key)
    {
        try
        {
            return LocString.GetIfExists(table, key);
        }
        catch
        {
            return null;
        }
    }

    private static string? Format(LocString loc)
    {
        try
        {
            string text = loc.GetFormattedText();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
