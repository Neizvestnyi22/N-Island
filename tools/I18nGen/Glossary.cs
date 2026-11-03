using System.Text.RegularExpressions;

/// <summary>
/// Термины, которые машинный перевод без контекста путает («Корзина» → Cart, «Сон» → Dream, «чёлка» → bangs).
/// Prepare заменяет «чёлку» на «остров» перед отправкой (Dynamic Island → island на всех языках),
/// Terms — готовые переводы коротких строк интерфейса.
/// </summary>
static class Glossary
{
    static readonly (Regex From, string To)[] Words =
    {
        (new Regex(@"\bчёлк(а|у)\b|\bчелк(а|у)\b"), "остров"),
        (new Regex(@"\bЧёлк(а|у)\b|\bЧелк(а|у)\b"), "Остров"),
        (new Regex(@"\bчёлки\b"), "острова"), (new Regex(@"\bЧёлки\b"), "Острова"),
        (new Regex(@"\bчёлке\b"), "острове"), (new Regex(@"\bЧёлке\b"), "Острове"),
        (new Regex(@"\bчёлкой\b"), "островом"), (new Regex(@"\bчёлок\b"), "островов"),
    };

    public static string Prepare(string text)
    {
        foreach (var (from, to) in Words) text = from.Replace(text, to);
        return text;
    }

    // ru → { язык → перевод }
    public static readonly Dictionary<string, Dictionary<string, string>> Terms = new()
    {
        ["Защита от слежки: включено"] = T("Anti-tracking: on", "Захист від стеження: увімкнено", "Tracking-Schutz: aktiv", "Antirrastreo: activo", "Anti-pistage : actif", "Antirrastreamento: ativo", "Ochrona przed śledzeniem: włączone", "İzlenme koruması: açık"),
        ["своб."] = T("free", "вільно", "frei", "libre", "libre", "livre", "wolne", "boş"),
        ["м/с · влажность"] = T("m/s · humidity", "м/с · вологість", "m/s · Feuchte", "m/s · humedad", "m/s · humidité", "m/s · umidade", "m/s · wilgotność", "m/s · nem"),
        ["Не спать"] = T("Keep awake", "Не спати", "Wach bleiben", "Mantener activo", "Rester éveillé", "Manter ativo", "Nie usypiaj", "Uyanık tut"),
        ["Блокировка"] = T("Lock", "Блокування", "Sperren", "Bloquear", "Verrouiller", "Bloquear", "Zablokuj", "Kilitle"),
        ["Основные"] = T("General", "Основні", "Allgemein", "General", "Général", "Geral", "Ogólne", "Genel"),
        ["Сделано"] = T("Made by", "Автори", "Gemacht von", "Hecho por", "Créé par", "Feito por", "Autorzy", "Yapımcılar"),
        ["из 100"] = T("of 100", "зі 100", "von 100", "de 100", "sur 100", "de 100", "na 100", "/ 100"),
        ["Вышла версия"] = T("New version", "Вийшла версія", "Neue Version", "Nueva versión", "Nouvelle version", "Nova versão", "Nowa wersja", "Yeni sürüm"),
        ["Безопасно"] = T("Safe", "Безпечно", "Sicher", "Seguro", "Sûr", "Seguro", "Bezpieczne", "Güvenli"),
        ["Корзина"] = T("Recycle Bin", "Кошик", "Papierkorb", "Papelera de reciclaje", "Corbeille", "Lixeira", "Kosz", "Geri Dönüşüm Kutusu"),
        ["Обзор"] = T("Overview", "Огляд", "Übersicht", "Resumen", "Vue d'ensemble", "Visão geral", "Przegląd", "Genel bakış"),
        ["Сон"] = T("Sleep", "Сон", "Energie sparen", "Suspender", "Veille", "Suspender", "Uśpienie", "Uyku"),
        ["Освобождено"] = T("Freed", "Звільнено", "Freigegeben", "Liberado", "Libéré", "Liberado", "Zwolniono", "Boşaltıldı"),
        ["Режим новичка"] = T("Beginner mode", "Режим новачка", "Anfängermodus", "Modo principiante", "Mode débutant", "Modo iniciante", "Tryb dla początkujących", "Başlangıç modu"),
        ["Защита от слежки"] = T("Anti-tracking", "Захист від стеження", "Tracking-Schutz", "Protección antirrastreo", "Protection anti-pistage", "Proteção contra rastreamento", "Ochrona przed śledzeniem", "İzlenme koruması"),
        ["Глубокая очистка"] = T("Deep cleanup", "Глибоке очищення", "Tiefenreinigung", "Limpieza profunda", "Nettoyage approfondi", "Limpeza profunda", "Głębokie czyszczenie", "Derin temizlik"),
        ["Обычный"] = T("Normal", "Звичайний", "Normal", "Normal", "Normal", "Normal", "Normalny", "Normal"),
        ["Починить"] = T("Fix", "Виправити", "Reparieren", "Reparar", "Réparer", "Reparar", "Napraw", "Onar"),
        ["Паспорт ноутбука"] = T("Laptop info", "Паспорт ноутбука", "Laptop-Steckbrief", "Ficha del portátil", "Fiche du portable", "Ficha do notebook", "Metryka laptopa", "Dizüstü bilgileri"),
        ["Центр управления ноутбуком"] = T("Laptop Control Center", "Центр керування ноутбуком", "Laptop-Kontrollzentrum", "Centro de control del portátil", "Centre de contrôle du portable", "Central de controle do notebook", "Centrum sterowania laptopem", "Dizüstü Kontrol Merkezi"),
        ["Журнал активности"] = T("Activity journal", "Журнал активності", "Aktivitätsprotokoll", "Registro de actividad", "Journal d'activité", "Registro de atividade", "Dziennik aktywności", "Etkinlik günlüğü"),
        ["Полка"] = T("Shelf", "Полиця", "Ablage", "Estante", "Étagère", "Prateleira", "Półka", "Raf"),
        ["Намаз"] = T("Prayer times", "Намаз", "Gebetszeiten", "Horarios de oración", "Heures de prière", "Horários de oração", "Czasy modlitw", "Namaz vakitleri"),
        ["Пульт"] = T("Controls", "Пульт", "Steuerung", "Controles", "Contrôles", "Controles", "Sterowanie", "Kontroller"),
        ["Лучше оставить"] = T("Keep it", "Краще залишити", "Besser behalten", "Mejor dejarlo", "Mieux vaut garder", "Melhor manter", "Lepiej zostawić", "Kalsın"),
        ["Можно отключить"] = T("Can be disabled", "Можна вимкнути", "Kann aus", "Se puede desactivar", "Peut être désactivé", "Pode desativar", "Można wyłączyć", "Kapatılabilir"),
        ["админ"] = T("admin", "адмін", "Admin", "admin", "admin", "admin", "admin", "yönetici"),
        ["Скорость"] = T("Speed", "Швидкість", "Leistung", "Velocidad", "Vitesse", "Velocidade", "Szybkość", "Hız"),
        ["Оптимизация"] = T("Optimization", "Оптимізація", "Optimierung", "Optimización", "Optimisation", "Otimização", "Optymalizacja", "Optimizasyon"),
        ["Вид Windows"] = T("Windows look", "Вигляд Windows", "Windows-Aussehen", "Aspecto de Windows", "Apparence de Windows", "Aparência do Windows", "Wygląd Windows", "Windows görünümü"),
        ["Как пользоваться"] = T("How to use", "Як користуватися", "So funktioniert's", "Cómo usar", "Mode d'emploi", "Como usar", "Jak używać", "Nasıl kullanılır"),
        ["Чёлка"] = T("Island", "Острівець", "Insel", "Isla", "Île", "Ilha", "Wyspa", "Ada"),
        ["Свернуть"] = T("Minimize", "Згорнути", "Minimieren", "Minimizar", "Réduire", "Minimizar", "Minimalizuj", "Küçült"),
        ["Закрыть"] = T("Close", "Закрити", "Schließen", "Cerrar", "Fermer", "Fechar", "Zamknij", "Kapat"),
        ["Сервисы"] = T("Services", "Сервіси", "Dienste", "Servicios", "Services", "Serviços", "Usługi", "Servisler"),
        ["Интеграции"] = T("Integrations", "Інтеграції", "Integrationen", "Integraciones", "Intégrations", "Integrações", "Integracje", "Entegrasyonlar"),
        ["Быстрый поиск"] = T("Quick search", "Швидкий пошук", "Schnellsuche", "Búsqueda rápida", "Recherche rapide", "Busca rápida", "Szybkie wyszukiwanie", "Hızlı arama"),
        ["Без дублей"] = T("No duplicates", "Без дублів", "Keine Duplikate", "Sin duplicados", "Sans doublons", "Sem duplicatas", "Bez duplikatów", "Kopya yok"),
        ["VPN выключен"] = T("VPN is off", "VPN вимкнено", "VPN aus", "VPN desactivada", "VPN désactivé", "VPN desligada", "VPN wyłączony", "VPN kapalı"),
        ["VPN включён"] = T("VPN is on", "VPN увімкнено", "VPN an", "VPN activada", "VPN activé", "VPN ligada", "VPN włączony", "VPN açık"),
        ["Повітряна тривога (alerts.in.ua)"] = T("Air raid alert (alerts.in.ua)", "Повітряна тривога (alerts.in.ua)", "Luftalarm (alerts.in.ua)", "Alerta aérea (alerts.in.ua)", "Alerte aérienne (alerts.in.ua)", "Alerta aéreo (alerts.in.ua)", "Alarm lotniczy (alerts.in.ua)", "Hava saldırısı alarmı (alerts.in.ua)"),
        ["Нова пошта"] = T("Nova Poshta", "Нова пошта", "Nova Poshta", "Nova Poshta", "Nova Poshta", "Nova Poshta", "Nova Poshta", "Nova Poshta"),
    };

    static Dictionary<string, string> T(string en, string uk, string de, string es, string fr, string pt, string pl, string tr) =>
        new() { ["en"] = en, ["uk"] = uk, ["de"] = de, ["es"] = es, ["fr"] = fr, ["pt"] = pt, ["pl"] = pl, ["tr"] = tr };
}
