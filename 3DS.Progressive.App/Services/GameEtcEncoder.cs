using System.Drawing;
using System.IO;
using pk3DS.Core.CTR;

namespace ThreeDS.Progressive.Services;
internal static class GameEtcEncoder
{
    private static readonly int[,] Modifiers={{2,8,-2,-8},{5,17,-5,-17},{9,29,-9,-29},{13,42,-13,-42},{18,60,-18,-60},{24,80,-24,-80},{33,106,-33,-106},{47,183,-47,-183}};
    internal static byte[] Encode(Bitmap bitmap,XLIMOrienter orienter,bool alpha)
    {
        using var output=new MemoryStream();using var writer=new BinaryWriter(output);
        var colors=new Color[16];
        for(uint tile=0;tile<orienter.Width*orienter.Height/64;tile++)for(uint block=0;block<4;block++)
        {
            ulong alphas=0;
            for(int x=0;x<4;x++)for(int y=0;y<4;y++)
            {
                uint morton=(uint)((x&1)|((y&1)<<1)|((x&2)<<1)|((y&2)<<2));var at=orienter.Get(tile*64+block*16+morton);
                Color c=at.X<bitmap.Width&&at.Y<bitmap.Height?bitmap.GetPixel((int)at.X,(int)at.Y):Color.Transparent;
                colors[x*4+y]=c;alphas|=(ulong)((c.A+8)/17)<<(4*(x*4+y));
            }
            var vertical=Compress(colors,false);var horizontal=Compress(colors,true);
            if(alpha)writer.Write(alphas);writer.Write(vertical.Error<=horizontal.Error?vertical.Block:horizontal.Block);
        }
        return output.ToArray();
    }
    private static (ulong Block,long Error) Compress(Color[] colors,bool flip)
    {
        uint high=flip?1u:0u,low=0;long total=0;
        for(int half=0;half<2;half++)
        {
            var indices=Enumerable.Range(0,16).Where(i=>((flip?i%4:i/4)>=2?1:0)==half).ToArray();
            int r=(int)Math.Round(indices.Average(i=>colors[i].R)/17),g=(int)Math.Round(indices.Average(i=>colors[i].G)/17),b=(int)Math.Round(indices.Average(i=>colors[i].B)/17);
            long best=long.MaxValue;int bestR=0,bestG=0,bestB=0,bestTable=0;uint bestLow=0;
            for(int rr=Math.Max(0,r-1);rr<=Math.Min(15,r+1);rr++)for(int gg=Math.Max(0,g-1);gg<=Math.Min(15,g+1);gg++)for(int bb=Math.Max(0,b-1);bb<=Math.Min(15,b+1);bb++)for(int table=0;table<8;table++)
            {
                long error=0;uint bits=0;
                foreach(int i in indices)
                {
                    long pixelBest=long.MaxValue;int chosen=0;
                    for(int selector=0;selector<4;selector++)
                    {
                        int modifier=Modifiers[table,selector],dr=colors[i].R-Math.Clamp(rr*17+modifier,0,255),dg=colors[i].G-Math.Clamp(gg*17+modifier,0,255),db=colors[i].B-Math.Clamp(bb*17+modifier,0,255);
                        long e=dr*dr+dg*dg+db*db;if(e<pixelBest){pixelBest=e;chosen=selector;}
                    }
                    error+=pixelBest;bits|=(uint)(chosen&1)<<i|(uint)(chosen>>1)<<(i+16);
                }
                if(error<best){best=error;bestR=rr;bestG=gg;bestB=bb;bestTable=table;bestLow=bits;}
            }
            high|=(uint)bestR<<(half==0?28:24)|(uint)bestG<<(half==0?20:16)|(uint)bestB<<(half==0?12:8)|(uint)bestTable<<(half==0?5:2);low|=bestLow;total+=best;
        }
        return ((ulong)high<<32|low,total);
    }
}
