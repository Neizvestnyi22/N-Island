using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NIsland.Core;

namespace NIsland.Services
{
    // =====================================================================
    //  Нова пошта: статус, срок хранения, сумма к оплате, ТТН из буфера обмена
    // =====================================================================

    public class NovaPoshtaHub : Integration
    {
        public override string Key => "novaposhta";
        public override string Name => "Нова пошта";
        public override string Glyph => "";
        public override string Color => "#DA291C";
        public override string Desc => "Следит за посылками: сообщает, когда посылка прибыла в отделение или почтомат, сколько платить при получении и с какого дня хранение станет платным. " +
                                       "Скопировал номер ТТН — остров предложит его отслеживать";
        public override string Help => "Номера ТТН по одному в строке. Через пробел — телефон получателя или отправителя (тогда видно отделение, сумму и сроки) и своё название: " +
                                       "«20450000000000 0671234567 Кроссовки». API-ключ не обязателен";
        public override string HelpUrl => "https://novaposhta.ua/tracking/";
        public override HubField[] Fields => new[]
        {
            new HubField("ttn", "Номера ТТН", "20400048799000 0671234567 Кроссовки", "multi"),
            new HubField("phone", "Мой телефон", "0671234567 — подставится ко всем ТТН", Required: false),
            new HubField("key", "API-ключ", "необязательно", "secret", Required: false),
            new HubField("clipboard", "Предлагать ТТН из буфера обмена", "", "bool", Required: false, Default: "1"),
            new HubField("hide", "Прятать полученные через 3 дня", "", "bool", Required: false, Default: "1"),
        };
        public override string[] DefaultSend => new[] { "parcel" };
        public override int PollSeconds => 900;

        record Ttn(string Number, string Phone, string Label);

        IEnumerable<Ttn> Items() => (V("ttn") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Where(p => p.Length > 0 && Regex.IsMatch(p[0], @"^\d{11,20}$"))
            .Select(p =>
            {
                string phone = p.Length > 1 && Regex.IsMatch(p[1], @"^\+?\d{9,13}$") ? p[1] : V("phone");
                int labelFrom = p.Length > 1 && Regex.IsMatch(p[1], @"^\+?\d{9,13}$") ? 2 : 1;
                return new Ttn(p[0], Regex.Replace(phone ?? "", @"\D", ""), string.Join(" ", p.Skip(labelFrom)));
            }).Take(30);

        static string Money(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? v.ToString("0.##", CultureInfo.InvariantCulture) + " ₴" : null;

        public override async Task Poll()
        {
            var list = Items().ToList();
            if (list.Count == 0) { Rows = new(); Status = "Нет посылок"; return; }
            using var doc = await Integrations.PostJson("https://api.novaposhta.ua/v2.0/json/", new
            {
                apiKey = V("key"), modelName = "TrackingDocument", calledMethod = "getStatusDocuments",
                methodProperties = new { Documents = list.Select(t => new { DocumentNumber = t.Number, Phone = t.Phone }).ToArray() },
            });
            if (doc.RootElement.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False && doc.RootElement.TryGetProperty("errors", out var errs) && errs.GetArrayLength() > 0)
                throw new InvalidOperationException(string.Join("; ", errs.EnumerateArray().Select(e => e.ToString())));
            var rows = new List<HubRow>();
            int waiting = 0;
            foreach (var d in doc.RootElement.GetProperty("data").EnumerateArray())
            {
                var num = Integrations.S(d, "Number");
                var t = list.FirstOrDefault(x => x.Number == num);
                var status = Integrations.S(d, "Status");
                int.TryParse(Integrations.S(d, "StatusCode"), out var code);
                string S(string k) => Integrations.S(d, k);
                bool arrived = code is 7 or 8, received = code is 9 or 10 or 11, refused = code is 102 or 103 or 108 or 105;
                if (arrived) waiting++;

                var parts = new List<string>();
                if (S("WarehouseRecipient") is { Length: > 0 } wh) parts.Add(wh);
                else if (S("CityRecipient") is { Length: > 0 } city) parts.Add(city);
                if (!received && S("ScheduledDeliveryDate") is { Length: > 0 } eta) parts.Add("очікується " + ShortDate(eta));
                var pay = new[] { S("PayerType") == "Recipient" ? Money(S("DocumentCost")) : null, Money(S("AfterpaymentOnGoodsCost")) }.Where(x => x != null).ToList();
                if (pay.Count > 0 && !received) parts.Add("до сплати " + string.Join(" + ", pay));
                if (arrived && S("DatePayedKeeping") is { Length: > 0 } keep) parts.Add("безкоштовно до " + ShortDate(keep));
                if (received && S("RecipientDateTime") is { Length: > 0 } got) parts.Add("отримано " + ShortDate(got));
                if (S("CargoDescriptionString") is { Length: > 0 } cargo && t?.Label == "") parts.Add(cargo);

                // полученные несколько дней назад прячем, чтобы список не разрастался
                if (received && Flag("hide") && DateTime.TryParse(S("RecipientDateTime"), CultureInfo.GetCultureInfo("uk-UA"), DateTimeStyles.None, out var gotAt) && (DateTime.Now - gotAt).TotalDays > 3) continue;

                var number = num;
                rows.Add(new HubRow
                {
                    Title = (arrived ? "📦 " : received ? "✓ " : refused ? "↩ " : "🚚 ") + (string.IsNullOrEmpty(t?.Label) ? num : t.Label + " · " + num[^4..]) + " — " + status,
                    Sub = string.Join(" · ", parts), Url = "https://novaposhta.ua/tracking/?cargo_number=" + num,
                    ActionText = "Убрать", Action = () => { Remove(number); return Task.FromResult("Убрано из списка"); },
                });

                var key = "st:" + num;
                var prev = St(key);
                if (prev != null && prev != code + "|" + status)
                {
                    var title = (string.IsNullOrEmpty(t?.Label) ? "Нова пошта · " + num : t.Label) + (arrived ? " — прибула!" : "");
                    Notify(title, status + (parts.Count > 0 ? " · " + string.Join(" · ", parts.Take(3)) : ""), "https://novaposhta.ua/tracking/?cargo_number=" + num);
                    if (arrived) System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => Sound.Play(SoundEvent.Reminder)));
                    Integrations.Publish("parcel", title, status + (parts.Count > 0 ? "\n" + string.Join("\n", parts) : ""));
                }
                if (prev != code + "|" + status) SetSt(key, code + "|" + status);
            }
            Rows = rows;
            Status = waiting > 0 ? $"Чекає в відділенні: {waiting}" : "Оновлено " + DateTime.Now.ToString("HH:mm");
        }

        static string ShortDate(string s) =>
            DateTime.TryParse(s, CultureInfo.GetCultureInfo("uk-UA"), DateTimeStyles.None, out var d) || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
                ? d.ToString(d.TimeOfDay == TimeSpan.Zero ? "d MMM" : "d MMM HH:mm", Fmt.Ru) : s;

        void Remove(string number)
        {
            var keep = (V("ttn") ?? "").Split('\n').Where(l => !l.TrimStart().StartsWith(number)).ToList();
            Integrations.SetValue(this, "ttn", string.Join("\n", keep).Trim());
            Rows.RemoveAll(r => r.Url?.EndsWith(number) == true);
            Integrations.RaiseChanged();
        }

        public void Add(string number)
        {
            if (Items().Any(t => t.Number == number)) return;
            Integrations.SetValue(this, "ttn", ((V("ttn") ?? "").Trim() + "\n" + number).Trim());
            if (!Enabled) Integrations.SetEnabled(this, true);
            _ = Integrations.PollOne(this);
        }

        static readonly Regex TtnRx = new(@"^\s*(20|59|1)\d{12,13}\s*$");

        /// <summary>Скопировали номер ТТН — предлагаем отслеживать (один раз на номер).</summary>
        public static void OnCopied(string text)
        {
            if (text == null || !TtnRx.IsMatch(text)) return;
            if (Integrations.Get("novaposhta") is not NovaPoshtaHub np || (np.Enabled && !np.Flag("clipboard"))) return;
            var num = text.Trim();
            if (np.Items().Any(t => t.Number == num)) return;
            App.Island?.ShowActivity(new Activity
            {
                Kind = "hub", Glyph = np.Glyph, Tint = ColorUtil.Parse(np.Color, System.Windows.Media.Colors.Red), Title = "Відстежувати посилку?", Sub = "ТТН " + num,
                Seconds = 8, Wide = true, ActionText = "Відстежувати", Action = () => { np.Add(num); App.Island?.Toast(np.Glyph, "Посилка додана", "ТТН " + num); },
            });
        }
    }
}
