using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Chess;
namespace NIsland.Core
{
    public sealed partial class FriendGames
    {
        static Game[] ChessCardCatalog=>new Game[]{
            new("chess","♟ Шахматы · preview","Выбери фигуру и клетку. Превращение: Q/R/B/N. Ничья автоматически при 3 повторах или 50 ходах без пешек/взятий. Без часов; редкие мёртвые позиции не распознаются."),
            new("eights","🃏 Восьмёрки","По 5 карт. Клади ту же масть или ранг; восьмёрка меняет масть. Нет хода — возьми карту и пропусти ход. Первым избавься от карт."),
            new("blackjack","♠ Двадцать одно","По очереди: ещё карту или стоп. Туз 1/11, картинки 10. Ближе к 21 — победа; перебор проигрывает. Без ставок, один раунд.")
        };
        static readonly string[] Suits={"♠","♥","♦","♣"};
        static string CardName(int n)=>new[]{"A","2","3","4","5","6","7","8","9","10","J","Q","K"}[n%13]+Suits[n/13];
        static int[] ShuffledDeck()
        {
            var d=Enumerable.Range(0,52).ToArray();for(int i=d.Length-1;i>0;i--){int j=RandomNumberGenerator.GetInt32(i+1);(d[i],d[j])=(d[j],d[i]);}return d;
        }
        static int Total(IEnumerable<int> hand)
        {
            int aces=hand.Count(c=>c%13==0),value=hand.Sum(c=>c%13==0?11:Math.Min(10,c%13+1));while(value>21&&aces-->0)value-=10;return value;
        }
        static void InitChessCards(Room r)
        {
            if(r.Kind=="chess")
            {
                r.Fen=new ChessBoard().ToFen();r.Pot=-1;r.Promotion="q";
                r.Positions=new List<string>{ChessPositionKey(ChessBoard.LoadFromFen(r.Fen,AutoEndgameRules.None))};return;
            }
            if(r.Kind is not("eights" or "blackjack"))return;
            var deck=ShuffledDeck();int count=r.Kind=="eights"?5:2;
            r.Hands=new[]{deck.Take(count).ToArray(),deck.Skip(count).Take(count).ToArray()};r.Deck=deck.Skip(count*2).ToArray();r.Discard=Array.Empty<int>();r.Aux=new int[2];r.Pot=-1;
            if(r.Kind=="eights"){r.Discard=new[]{r.Deck[0]};r.Deck=r.Deck.Skip(1).ToArray();r.Pot=r.Discard[0]/13;}
        }
        internal static Move[] ChessLegal(ChessBoard b)
        {
            var moves=new List<Move>();for(short y=0;y<8;y++)for(short x=0;x<8;x++)if(b[x,y]?.Color==b.Turn)moves.AddRange(b.Moves(new Position(x,y),generateSan:false));return moves.ToArray();
        }
        internal static string ChessPositionKey(ChessBoard b)
        {
            var f=b.ToFen().Split(' ');if(f[3]!="-"&&!ChessLegal(b).Any(m=>m.IsEnPassant))f[3]="-";return string.Join(" ",f.Take(4));
        }
        internal static bool ChessDead(ChessBoard b)
        {
            var minor=new List<(char Piece,int Color)>();
            for(int y=0;y<8;y++)for(int x=0;x<8;x++){var p=b[x,y];if(p==null||p.Type==PieceType.King)continue;char c=char.ToLowerInvariant(p.ToFenChar());if(c is not('b' or 'n'))return false;minor.Add((c,(x+y)%2));}
            return minor.Count<=1||minor.All(p=>p.Piece=='b')&&minor.Select(p=>p.Color).Distinct().Count()==1;
        }
        static string Square(int index)=>((char)('a'+index%8)).ToString()+(8-index/8);
        static int Index(Position pos)=>(7-pos.Y)*8+pos.X;
        static bool ChessMove(Room r,int p,string op,out bool extra)
        {
            extra=false;var b=ChessBoard.LoadFromFen(r.Fen,AutoEndgameRules.None);
            var moves=ChessLegal(b);
            if(op=="draw")
            {
                if(r.DrawOffer==1-p){r.Winner=0;r.DrawOffer=-1;}
                else{r.DrawOffer=p;extra=true;}return true;
            }
            if(op.Length==2&&op[0]=='p'&&"qrbn".Contains(op[1])){r.Promotion=op[1..];extra=true;return true;}
            if(op.StartsWith("s")&&int.TryParse(op[1..],out int selected)&&selected>=0&&selected<64&&moves.Any(m=>Index(m.OriginalPosition)==selected))
            {r.Pot=selected;extra=true;return true;}
            if(!int.TryParse(op,out int target)||target<0||target>=64||r.Pot<0||r.Pot>=64)return false;
            var move=moves.FirstOrDefault(m=>Index(m.OriginalPosition)==r.Pot&&Index(m.NewPosition)==target&&(!m.IsPromotion||char.ToLowerInvariant(m.Promotion.ToFenChar()).ToString()==r.Promotion));
            if(move==null||!b.Move(move))return false;
            r.Fen=b.ToFen();r.Pot=-1;r.DrawOffer=-1;r.ChessLast=move.San??Square(Index(move.OriginalPosition))+Square(target);r.Promotion="q";
            var key=ChessPositionKey(b);if(move.Piece.Type==PieceType.Pawn||move.CapturedPiece!=null)r.Positions.Clear();r.Positions.Add(key);
            var end=b.EndGame;
            if(end!=null)r.Winner=end.WonSide==null?0:end.WonSide==PieceColor.White?1:2;
            else if(ChessDead(b)||int.Parse(r.Fen.Split(' ')[4])>=100||r.Positions.Count(k=>k==key)>=3)r.Winner=0;
            return true;
        }
        static int[] EightsLegal(Room r,int p)=>r.Hands[p].Where(c=>c/13==r.Pot||c%13==r.Discard[^1]%13||c%13==7).ToArray();
        static void Recycle(Room r)
        {
            if(r.Deck.Length>0||r.Discard.Length<=1)return;
            var d=r.Discard[..^1];for(int i=d.Length-1;i>0;i--){int j=RandomNumberGenerator.GetInt32(i+1);(d[i],d[j])=(d[j],d[i]);}
            r.Deck=d;r.Discard=new[]{r.Discard[^1]};
        }
        static bool CardMove(Room r,int p,string op,out bool extra)
        {
            extra=false;
            if(op.StartsWith("page")&&int.TryParse(op[4..],out int page)&&page>=0&&page<Math.Max(1,(r.Hands[p].Length+7)/8)){r.CardPages[p]=page;extra=true;return true;}
            if(r.Kind=="blackjack")
            {
                if(r.Aux[p]!=0)return false;
                if(op=="hit"&&r.Deck.Length>0){r.Hands[p]=r.Hands[p].Append(r.Deck[0]).ToArray();r.Deck=r.Deck.Skip(1).ToArray();if(Total(r.Hands[p])>=21)r.Aux[p]=1;}
                else if(op=="stand")r.Aux[p]=1;else return false;
                if(r.Aux.All(v=>v==1)){int a=Total(r.Hands[0]),b=Total(r.Hands[1]);a=a>21?-1:a;b=b>21?-1:b;r.Winner=a==b?0:a>b?1:2;}
                else if(r.Aux[1-p]==1)extra=true;
                return true;
            }
            if(r.Last==1)
            {
                if(op.Length!=2||op[0]!='u'||!int.TryParse(op[1..],out int suit)||suit<0||suit>3)return false;
                r.Pot=suit;r.Last=0;if(r.Hands[p].Length==0)r.Winner=p+1;return true;
            }
            if(op=="draw")
            {
                if(EightsLegal(r,p).Length>0)return false;
                Recycle(r);
                if(r.Deck.Length>0){r.Hands[p]=r.Hands[p].Append(r.Deck[0]).ToArray();r.Deck=r.Deck.Skip(1).ToArray();r.Passes=0;}
                else if(++r.Passes>=2)r.Winner=0;
                return true;
            }
            if(!int.TryParse(op,out int card)||!EightsLegal(r,p).Contains(card))return false;
            r.Hands[p]=r.Hands[p].Where(c=>c!=card).ToArray();r.Discard=r.Discard.Append(card).ToArray();r.Pot=card/13;r.Passes=0;
            if(card%13==7){r.Last=1;extra=true;}else if(r.Hands[p].Length==0)r.Winner=p+1;
            return true;
        }
        static object ChessView(Room r,long user,Func<string,string> t)
        {
            int p=user==r.A?0:1;var b=ChessBoard.LoadFromFen(r.Fen,AutoEndgameRules.None);var game=ChessCardCatalog[0];bool can=r.B>0&&r.Winner==-1&&r.Turn==p;
            var moves=can?ChessLegal(b):Array.Empty<Move>();
            string[] ops=new string[64];var pieces=new char[64];
            for(int i=0;i<64;i++)
            {
                pieces[i]=b[i%8,7-i/8]?.ToFenChar()??'.';
                ops[i]=moves.Any(m=>Index(m.OriginalPosition)==i)?"s"+i:moves.Any(m=>Index(m.OriginalPosition)==r.Pot&&Index(m.NewPosition)==i)?i.ToString():"";
            }
            var status=r.B==0?t("Ожидание друга"):r.Winner==0?t("Ничья"):r.Winner>0?t("Победил игрок")+" "+(r.Winner==1?"♔":"♚"):can?t("Твой ход"):t("Ход соперника");
            return new{id=r.Id,k=r.Kind,h=t(game.Title),s=status,i=(b.WhiteKingChecked||b.BlackKingChecked?t("Шах")+" · ":"")+(r.ChessLast??"")+(r.DrawOffer>=0?" · ½?":""),z=new string(pieces),o=ops,c=8,v=r.Revision,done=r.Winner!=-1,joined=r.B>0,me=p==0?"♔":"♚",flip=p==1,sel=r.Pot,prom=r.Promotion,can,rule=t(game.Rules)};
        }
        static void CardsView(Room r,int p,Func<string,string> t,Action<string,string> button,out string info)
        {
            var own=r.Hands[p];bool ownDone=r.Kind=="blackjack"&&r.Aux[p]!=0;
            if(r.Kind=="eights"&&r.Last==1){for(int i=0;i<4;i++)button(Suits[i],"u"+i);}
            else
            {
                var legal=r.Kind=="eights"?EightsLegal(r,p):Array.Empty<int>();
                int page=Math.Clamp(r.CardPages[p],0,Math.Max(0,(own.Length-1)/8));
                foreach(int c in own.Skip(page*8).Take(8))button(CardName(c),legal.Contains(c)?c.ToString():"");
                if(own.Length>8){if(page>0)button("◀","page"+(page-1));if((page+1)*8<own.Length)button("▶","page"+(page+1));}
                if(r.Kind=="eights"&&legal.Length==0)button(t("Взять карту"),"draw");
                if(r.Kind=="blackjack"&&!ownDone){button(t("Ещё"),"hit");button(t("Стоп"),"stand");}
            }
            info=r.Kind=="eights"?CardName(r.Discard[^1])+" · "+Suits[r.Pot]+" · "+t("Карт у соперника")+": "+r.Hands[1-p].Length:
                t("Твои очки")+": "+Total(own)+" · "+(r.Winner!=-1?string.Join(" ",r.Hands[1-p].Select(CardName))+" = "+Total(r.Hands[1-p]):t("Карт у соперника")+": "+r.Hands[1-p].Length);
        }
    }
}
