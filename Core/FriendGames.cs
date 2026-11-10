using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace NIsland.Core
{
    /// <summary>Isolated two-player rules/state. No PC APIs, processes, files supplied by users or host commands.</summary>
    public sealed partial class FriendGames
    {
        public record Game(string Key, string Title, string Rules);
        public static readonly Game[] Catalog = new Game[] {
            new("ttt", "❌ Крестики-нолики онлайн", "Собери три в ряд на поле 3×3."),
            new("four", "🔴 Четыре в ряд", "Выбирай колонку. Собери четыре в ряд."),
            new("reversi", "⚫ Реверси 4×4", "Зажимай фишки соперника. Если ходов нет, ход пропускается."),
            new("nim", "🪵 Ним", "Убери любое число камней из одной кучи. Последний камень побеждает."),
            new("take21", "🧮 Двадцать один", "Убери 1–3 камня. Взявший последний побеждает."),
            new("race", "🏁 Гонка до 30", "Бросай кубик по очереди. Первым набери 30."),
            new("guess", "🔍 Найди число", "По очереди ищите число от 1 до 20. Бот подсказывает больше или меньше."),
            new("pig", "🐷 Риск", "Бросай или забери очки. Единица сжигает очки хода. Первым набери 30."),
            new("rps", "✊ Камень, ножницы, бумага", "Выбор скрыт до ответа соперника. Одна партия."),
            new("sea", "🚢 Морской бой 4×4", "Три однопалубных корабля расставлены случайно. Попадание даёт ещё один выстрел."),
            new("dots", "🔲 Квадраты", "Добавляй рёбра. Замкни квадрат — получишь очко и ещё один ход. Поле 2×2.")
        }.Concat(NewExtraCatalog()).Concat(ChessCardCatalog).ToArray();
        public sealed class Room
        {
            public string Id { get; set; }
            public string Kind { get; set; }
            public long A { get; set; }
            public long B { get; set; }
            public long MessageA { get; set; }
            public long MessageB { get; set; }
            public int Turn { get; set; }
            public int Winner { get; set; } = -1; // -1 ongoing, 0 draw, 1 A, 2 B
            public int Revision { get; set; }
            public DateTime Expires { get; set; }
            public string LanguageA { get; set; } = "ru";
            public string LanguageB { get; set; } = "ru";
            public int[] Board { get; set; } = Array.Empty<int>();
            public int[] Aux { get; set; } = Array.Empty<int>();
            public int[] Scores { get; set; } = new int[2];
            public int Pot { get; set; }
            public int Secret { get; set; }
            public int Low { get; set; } = 1;
            public int High { get; set; } = 20;
            public int Last { get; set; }
            public int Passes { get; set; }
            public string Fen { get; set; }
            public string Promotion { get; set; }="q";
            public string ChessLast { get; set; }
            public List<string> Positions { get; set; }=new();
            public int DrawOffer { get; set; }=-1;
            public int[] Deck { get; set; }=Array.Empty<int>();
            public int[] Discard { get; set; }=Array.Empty<int>();
            public int[][] Hands { get; set; }=new[]{Array.Empty<int>(),Array.Empty<int>()};
            public int[] CardPages { get; set; }=new int[2];
        }
        readonly Dictionary<string, Room> rooms = new();
        readonly string file;
        public FriendGames(string file = null)
        {
            this.file = file;
            if (file == null || !File.Exists(file)) return;
            // A corrupted file must fail closed, not reset rooms and accept arbitrary moves.
            var saved = JsonSerializer.Deserialize<List<Room>>(File.ReadAllText(file)) ?? new();
            foreach(var room in saved.Where(r=>r!=null&&r.Expires>DateTime.UtcNow).Take(100))
                if (Valid(room)) rooms[room.Id] = room;
        }
        static bool Valid(Room r)
        {
            if(r==null||r.Id?.Length!=24||!r.Id.All(c=>c is >= 'a' and <= 'f' or >= '0' and <= '9')||r.A<=0||r.B<0||r.B==r.A||r.Turn is <0 or >1||r.Winner is < -1 or >2||r.Revision<0||r.Board==null||r.Aux==null||r.Scores?.Length!=2||r.Scores.Any(v=>v<0)||!Catalog.Any(g=>g.Key==r.Kind))return false;
            int length=r.Kind switch{"ttt" or "sos" or "numberttt"=>9,"four"=>42,"reversi" or "hex" or "domineering" or "chomp" or "obstruction" or "isolation" or "breakthrough"=>16,"sea"=>32,"dots" or "kayles" or "silver"=>12,"nim"=>3,"gomoku"=>25,"wythoff" or "euclid"=>2,"mancala"=>8,"treble"=>13,_=>0};
            if(r.Board.Length!=length||r.Board.Any(v=>v<0||v>100))return false;
            if(r.Kind=="sea"&&(r.Aux.Length!=32||r.Aux.Any(v=>v is <0 or >1)))return false;
            if(r.Kind=="rps"&&(r.Aux.Length!=2||r.Aux.Any(v=>v is < -1 or >2)))return false;
            if(r.Kind=="dots"&&r.Aux.Length!=4)return false;
            if(r.Kind=="breakthrough"&&(r.Aux.Length!=1||r.Aux[0] is < -1 or >15))return false;
            if(r.Kind=="isolation"&&(r.Board.Count(v=>v==1)!=1||r.Board.Count(v=>v==2)!=1||r.Pot is <0 or >1))return false;
            if(r.Kind=="silver"&&r.Pot is < -1 or >11)return false;
            if(r.Kind=="sos"&&r.Pot is <1 or >2)return false;
            if(r.Kind=="numberttt"&&r.Pot is <0 or >9)return false;
            if(r.Kind=="chess")
            {
                if(r.Fen==null||r.Fen.Length>100||r.Pot is < -1 or >63||!new[]{"q","r","b","n"}.Contains(r.Promotion)||r.Positions==null||r.Positions.Count>1000||r.DrawOffer is < -1 or >1)return false;
                try {var b=Chess.ChessBoard.LoadFromFen(r.Fen);if((b.Turn==Chess.PieceColor.White?0:1)!=r.Turn)return false;}catch{return false;}
            }
            if(r.Kind is "eights" or "blackjack")
            {
                if(r.Hands?.Length!=2||r.Hands.Any(h=>h==null)||r.Deck==null||r.Discard==null||r.Aux.Length!=2||r.Aux.Any(v=>v is <0 or >1)||r.CardPages?.Length!=2||r.CardPages.Any(v=>v is <0 or >6))return false;
                var all=r.Deck.Concat(r.Discard).Concat(r.Hands[0]).Concat(r.Hands[1]).ToArray();
                if(all.Length!=52||all.Distinct().Count()!=52||all.Any(c=>c<0||c>51))return false;
                if(r.Kind=="eights"&&(r.Discard.Length==0||r.Pot is <0 or >3||r.Last is <0 or >1))return false;
            }
            return true;
        }
        public Room Get(string id) => id != null && rooms.TryGetValue(id,out var r) && r.Expires>DateTime.UtcNow ? r : null;
        public Room Pending(string kind,long owner)=>rooms.Values.FirstOrDefault(r=>r.Kind==kind&&r.A==owner&&r.B==0&&r.Winner==-1&&r.Expires>DateTime.UtcNow);
        public Room Create(string kind,long owner,string language="ru")
        {
            foreach(var id in rooms.Where(p=>p.Value.Expires<=DateTime.UtcNow).Select(p=>p.Key).ToArray()) rooms.Remove(id);
            if(owner<=0||!Catalog.Any(g=>g.Key==kind)||rooms.Count>=100||rooms.Values.Count(r=>r.A==owner&&r.Winner==-1)>=12) return null;
            var r=new Room{Id=Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant(),Kind=kind,A=owner,LanguageA=Lang(language),Expires=DateTime.UtcNow.AddHours(24)};
            r.Board=new int[kind switch{"ttt"=>9,"four"=>42,"reversi"=>16,"sea"=>32,"dots"=>12,"nim"=>3,_=>0}];
            if(kind=="nim")r.Board=new[]{3,5,7};
            if(kind=="take21")r.Pot=21;
            if(kind=="guess")r.Secret=Roll(20);
            if(kind=="rps")r.Aux=new[]{-1,-1};
            if(kind=="reversi"){r.Board[5]=r.Board[10]=1;r.Board[6]=r.Board[9]=2;}
            if(kind=="sea"){r.Aux=new int[32];for(int p=0;p<2;p++){var cells=Enumerable.Range(p*16,16).OrderBy(_=>RandomNumberGenerator.GetInt32(int.MaxValue)).Take(3);foreach(var i in cells)r.Aux[i]=1;}}
            if(kind=="dots")r.Aux=new int[4];
            InitExtra(r);InitChessCards(r);rooms[r.Id]=r;Save();return r;
        }
        public static string Lang(string language) => language?.StartsWith("uk")==true?"uk":language?.StartsWith("ru")==true?"ru":"en";
        public bool Join(string id,long user,string language)
        {
            var r=Get(id);if(r==null||user<=0||r.A==user||r.B!=0)return false;
            r.B=user;r.LanguageB=Lang(language);r.Revision++;Save();return true;
        }
        public void RememberMessage(Room r,long user,long id){if(user==r.A)r.MessageA=id;else if(user==r.B)r.MessageB=id;Save();}
        public bool Move(string id,long user,int revision,string op)
        {
            var r=Get(id);if(r==null||r.B==0||r.Winner!=-1||revision!=r.Revision)return false;
            int player=user==r.A?0:user==r.B?1:-1;if(player<0)return false;
            if(op=="resign"){r.Winner=2-player;r.Revision++;Save();return true;}
            if(r.Kind=="chess"&&op=="draw")
            {
                if(r.DrawOffer==1-player){r.Winner=0;r.DrawOffer=-1;}
                else if(player==r.Turn)r.DrawOffer=player;else return false;
                r.Revision++;Save();return true;
            }
            if(r.Kind!="rps"&&player!=r.Turn)return false;
            bool extra=false;int n;int mark=player+1;
            switch(r.Kind)
            {
                case "ttt":
                    if(!int.TryParse(op,out n)||n<0||n>=9||r.Board[n]!=0)return false;r.Board[n]=mark;
                    if(Line(r.Board,3,3,mark))r.Winner=mark;else if(r.Board.All(v=>v!=0))r.Winner=0;break;
                case "four":
                    if(!int.TryParse(op,out n)||n<0||n>6)return false;
                    int cell=-1;for(int row=5;row>=0;row--)if(r.Board[row*7+n]==0){cell=row*7+n;break;}if(cell<0)return false;r.Board[cell]=mark;
                    if(Line(r.Board,7,4,mark))r.Winner=mark;else if(r.Board.All(v=>v!=0))r.Winner=0;break;
                case "reversi":
                    if(!int.TryParse(op,out n)||n<0||n>=16)return false;var flips=Flips(r.Board,n,mark);if(flips.Count==0)return false;r.Board[n]=mark;foreach(var i in flips)r.Board[i]=mark;
                    bool other=Enumerable.Range(0,16).Any(i=>Flips(r.Board,i,3-mark).Count>0),mine=Enumerable.Range(0,16).Any(i=>Flips(r.Board,i,mark).Count>0);
                    if(!other&&!mine){var a=r.Board.Count(v=>v==1);var b=r.Board.Count(v=>v==2);r.Winner=a==b?0:a>b?1:2;}else if(!other)extra=true;break;
                case "nim":
                    var parts=op.Split('.');if(parts.Length!=2||!int.TryParse(parts[0],out int heap)||heap<0||heap>2||!int.TryParse(parts[1],out n)||n<1||n>r.Board[heap])return false;
                    r.Board[heap]-=n;if(r.Board.Sum()==0)r.Winner=mark;break;
                case "take21":
                    if(!int.TryParse(op,out n)||n<1||n>3||n>r.Pot)return false;r.Pot-=n;if(r.Pot==0)r.Winner=mark;break;
                case "race":
                    if(op!="roll")return false;r.Last=Roll(6);r.Scores[player]+=r.Last;if(r.Scores[player]>=30)r.Winner=mark;break;
                case "guess":
                    if(!int.TryParse(op,out n)||n<r.Low||n>r.High)return false;r.Last=n;
                    if(n==r.Secret)r.Winner=mark;else if(n<r.Secret)r.Low=n+1;else r.High=n-1;break;
                case "pig":
                    if(op=="bank"){if(r.Pot<=0)return false;r.Scores[player]+=r.Pot;r.Pot=0;if(r.Scores[player]>=30)r.Winner=mark;}
                    else if(op=="roll"){r.Last=Roll(6);if(r.Last==1)r.Pot=0;else{r.Pot+=r.Last;extra=true;}}
                    else return false;break;
                case "rps":
                    if(!int.TryParse(op,out n)||n<0||n>2||r.Aux[player]>=0)return false;r.Aux[player]=n;
                    if(r.Aux.All(v=>v>=0)){int diff=(r.Aux[0]-r.Aux[1]+3)%3;r.Winner=diff==0?0:diff==1?1:2;}extra=true;break;
                case "sea":
                    if(!int.TryParse(op,out n)||n<0||n>=16)return false;int idx=(1-player)*16+n;if(r.Board[idx]!=0)return false;
                    r.Board[idx]=r.Aux[idx]==1?2:1;r.Last=n;extra=r.Board[idx]==2;
                    if(Enumerable.Range((1-player)*16,16).Count(i=>r.Board[i]==2)==3)r.Winner=mark;break;
                case "dots":
                    if(!int.TryParse(op,out n)||n<0||n>=12||r.Board[n]!=0)return false;r.Board[n]=mark;
                    for(int i=0;i<4;i++){int y=i/2,x=i%2;var edges=new[]{y*2+x,(y+1)*2+x,6+y*3+x,6+y*3+x+1};if(r.Aux[i]==0&&edges.All(e=>r.Board[e]!=0)){r.Aux[i]=mark;r.Scores[player]++;extra=true;}}
                    if(r.Aux.All(v=>v!=0))r.Winner=r.Scores[0]==r.Scores[1]?0:r.Scores[0]>r.Scores[1]?1:2;break;
                case "chess":if(!ChessMove(r,player,op,out extra))return false;break;
                case "eights":case "blackjack":if(!CardMove(r,player,op,out extra))return false;break;
                default:if(!MoveExtra(r,player,op,out extra))return false;break;
            }
            if(!extra)r.Turn=1-r.Turn;r.Revision++;Save();return true;
        }
        static int Roll(int max)=>RandomNumberGenerator.GetInt32(1,max+1);
        static bool Line(int[] board,int width,int need,int mark)
        {
            int height=board.Length/width;
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)foreach(var (dx,dy) in new[]{(1,0),(0,1),(1,1),(1,-1)})
            {int ex=x+(need-1)*dx,ey=y+(need-1)*dy;if(ex<0||ex>=width||ey<0||ey>=height)continue;if(Enumerable.Range(0,need).All(i=>board[(y+i*dy)*width+x+i*dx]==mark))return true;}
            return false;
        }
        static List<int> Flips(int[] board,int cell,int mark)
        {
            var all=new List<int>();if(board[cell]!=0)return all;
            for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
            {if(dx==0&&dy==0)continue;int x=cell%4+dx,y=cell/4+dy;var run=new List<int>();while(x>=0&&x<4&&y>=0&&y<4&&board[y*4+x]==3-mark){run.Add(y*4+x);x+=dx;y+=dy;}if(run.Count>0&&x>=0&&x<4&&y>=0&&y<4&&board[y*4+x]==mark)all.AddRange(run);}
            return all;
        }
        public object View(Room r,long user,Func<string,string> t)
        {
            int p=user==r.A?0:user==r.B?1:-1;if(p<0)throw new UnauthorizedAccessException();
            if(r.Kind=="chess")return ChessView(r,user,t);
            var game=Catalog.First(g=>g.Key==r.Kind);bool can=r.B>0&&r.Winner==-1&&(r.Kind=="rps"?r.Aux[p]<0:r.Turn==p);
            var labels=new List<string>();var ops=new List<string>();int columns=3;
            void Button(string label,string op){labels.Add(label);ops.Add(can?op:"");}
            switch(r.Kind)
            {
                case "ttt":case "four":case "reversi":
                    columns=r.Kind=="four"?7:r.Kind=="reversi"?4:3;
                    for(int i=0;i<r.Board.Length;i++){int v=r.Board[i];bool legal=r.Kind=="four"?r.Board[i%7]==0:r.Kind=="reversi"?Flips(r.Board,i,p+1).Count>0:v==0;Button(v==0?"·":v==1?"●":"○",legal?(r.Kind=="four"?i%7:i).ToString():"");}break;
                case "nim":
                    columns=7;for(int h=0;h<3;h++)for(int i=1;i<=7;i++)Button(i<=r.Board[h]?"−"+i:"·",i<=r.Board[h]?h+"."+i:"");break;
                case "take21":for(int i=1;i<=3;i++)Button("−"+i,i<=r.Pot?i.ToString():"");break;
                case "race":Button("🎲", "roll");break;
                case "pig":Button("🎲","roll");Button(t("Забрать очки"),r.Pot>0?"bank":"");break;
                case "guess":columns=5;for(int i=1;i<=20;i++)Button(i.ToString(),i>=r.Low&&i<=r.High?i.ToString():"");break;
                case "rps":columns=3;for(int i=0;i<3;i++)Button(new[]{"✊","📄","✂️"}[i],i.ToString());break;
                case "sea":columns=4;for(int i=0;i<16;i++){int idx=(1-p)*16+i;Button(r.Board[idx]==2?"💥":r.Board[idx]==1?"≈":"·",r.Board[idx]==0?i.ToString():"");}break;
                case "dots":columns=4;for(int i=0;i<12;i++)Button((i<6?"H":"V")+(i<6?i+1:i-5)+(r.Board[i]==1?"●":r.Board[i]==2?"○":""),r.Board[i]==0?i.ToString():"");break;
            }
            string extraInfo="";if(ExtraCatalog.Any(g=>g.Key==r.Kind)) ViewExtra(r,p,t,Button,ref columns,out extraInfo);
            if(r.Kind is "eights" or "blackjack")CardsView(r,p,t,Button,out extraInfo);
            string info=r.Kind switch{
                "nim"=>string.Join(" · ",r.Board),"take21"=>r.Pot.ToString(),"pig"=>r.Scores[0]+":"+r.Scores[1]+" · "+t("На кону")+" "+r.Pot+" · 🎲"+r.Last,
                "race"=>r.Scores[0]+":"+r.Scores[1]+" · 🎲"+r.Last,"guess"=>r.Low+"…"+r.High,
                "rps"=>r.Winner==-1?(r.Aux[p]>=0?t("Выбор принят"):""):new[]{"✊","📄","✂️"}[r.Aux[0]]+" : "+new[]{"✊","📄","✂️"}[r.Aux[1]],
                "sea"=>t("Твои корабли")+": "+string.Join(",",Enumerable.Range(p*16,16).Where(i=>r.Aux[i]==1).Select(i=>(i%16+1)+(r.Board[i]==2?"💥":""))),
                "dots"=>r.Scores[0]+":"+r.Scores[1]+" · "+string.Join("",r.Aux.Select(v=>v==1?"●":v==2?"○":"□")),_=>extraInfo};
            return new { id=r.Id,k=r.Kind,h=t(game.Title),s=r.B==0?t("Ожидание друга"):r.Winner==0?t("Ничья"):r.Winner>0?t("Победил игрок")+" "+(r.Winner==1?"●":"○"):can?t("Твой ход"):t("Ход соперника"),i=info,l=labels,o=ops,c=columns,v=r.Revision,done=r.Winner!=-1,joined=r.B>0,me=p==0?"●":"○",rule=t(game.Rules) };
        }
        public void Save()
        {
            if(file==null)return;Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file)));
            string tmp=file+".tmp";File.WriteAllText(tmp,JsonSerializer.Serialize(rooms.Values));File.Move(tmp,file,true);
        }
    }
}
