using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media.Imaging;
using ThreeDS.Progressive;
using ThreeDS.Progressive.Services;
internal static class DescriptionPreviewChecks
{
    internal static void Run(Action<bool,string> check,Action<Action,string> reject,string[] roots)
    {
        reject(()=>GameDescriptionFont.FromArchive(new byte[20],"fixture"),"missing font signature is rejected");
        reject(()=>GameDescriptionFont.FromArchive("CFNT"u8.ToArray(),"fixture"),"truncated font is rejected");
        for(int i=0;i<roots.Length;i++)
        {
            int generation=i<2?6:7;bool oras=i==1;string relative=generation==7?"a/0/2/5":oras?"a/1/6/7":"a/1/8/5";
            string rom=Path.Combine(roots[i],"ExtractedRomFS"),path=Path.Combine(rom,relative);byte[] original=File.ReadAllBytes(path);string hash=Convert.ToHexString(SHA256.HashData(original));
            var font=GameDescriptionFont.Load(rom,generation,oras);
            check("Protege al usuario.áéíóúñ¿¡".All(font.Contains),"original font includes Spanish description glyphs "+i);
            check(font.Advance('W')>font.Advance('i'),"preview uses original proportional glyph advances "+i);
            var rendered=font.Render("Protege al usuario.\nSi recibe contacto, baja 1 nivel\nel Ataque del rival.",generation);
            check(rendered.Image.PixelWidth==320&&rendered.Image.PixelHeight==112&&rendered.Image.IsFrozen,"preview renders frozen image at native reference resolution "+i);
            check(rendered.Status.StartsWith("3 línea(s)."),"explicit line breaks are preserved "+i);
            check(font.Render(new string('W',150),generation).Status.Contains("supera"),"long line warns without changing source text "+i);
            check(font.Render("Primero\nSegundo\nTercero\nCuarto\nQuinto\nSexto\nSéptimo\nOctavo",generation).Status.Contains("supera"),"vertical overflow is reported "+i);
            check(font.Render("\u0001",generation).Status.Contains("sin representación"),"unsupported characters have an explicit indication "+i);
            string output=Path.Combine(AppContext.BaseDirectory,"description-preview-"+i+".png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(rendered.Image));using(var file=File.Create(output))encoder.Save(file);
            if(generation==7)
            {
                var scene=GameMoveDescriptionScene.Load(rom,i==3);check(scene.DescriptionArea.Left==2&&scene.DescriptionArea.Top>90&&scene.DescriptionArea.Top<92&&scene.DescriptionArea.Width==318,"description uses the original centered pane anchor "+i);var hashes=new HashSet<string>();
                for(int type=0;type<18;type++)
                {
                    var card=scene.Render(font,"Protege al usuario.\nSi recibe contacto, baja 1 nivel\nel Ataque del rival.",new MovePreviewExample("Escudo Real",type,"Tipo",0,0,0,10));
                    check(card.Image.PixelWidth==320&&card.Image.PixelHeight==240&&card.Image.IsFrozen,"real move card renders type palette "+i+"/"+type);
                    byte[] pixels=new byte[320*240*4];card.Image.CopyPixels(pixels,320*4,0);hashes.Add(Convert.ToHexString(SHA256.HashData(pixels)));
                    if(type==8){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(card.Image));using var imageFile=File.Create(Path.Combine(AppContext.BaseDirectory,"move-card-"+i+".png"));png.Save(imageFile);}
                }
                check(hashes.Count>=16,"move card uses the ROM animation palettes rather than one generic background "+i);
                check(scene.Render(font,new string('W',100),new MovePreviewExample("Ejemplo",0,"Normal",1,40,100,35)).Status.Contains("supera"),"description overflow uses the actual layout box "+i);
            }
            if(generation==6)
            {
                string cardPath=Path.Combine(rom,"a/0/0/5");string cardHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cardPath)));
                var scene=GameMoveDescription6Scene.Load(rom);
                check(scene.DescriptionArea==new Rect(3,120,318,60),"Gen6 description uses the original move-detail pane "+i);
                for(int type=0;type<18;type++)
                {
                    var card=scene.Render(font,"Protege al usuario.\nSi recibe contacto, baja 1 nivel\nel Ataque del rival.",new MovePreviewExample("Escudo Real",type,"Tipo",0,0,0,10));
                    check(card.Image.PixelWidth==320&&card.Image.PixelHeight==240&&card.Image.IsFrozen,"Gen6 native move card supports type "+i+"/"+type);
                    if(type==8){var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(card.Image));using var imageFile=File.Create(Path.Combine(AppContext.BaseDirectory,"gen6-move-card-"+i+".png"));png.Save(imageFile);}
                    check(card.Status.Contains("cabe"),"three lines fit using Gen6 nominal font size "+i+"/"+type);
                }
                check(scene.Render(font,new string('W',100),new MovePreviewExample("Destructor",0,"Normal",1,40,100,35)).Status.Contains("supera"),"Gen6 long text overflows the real description area "+i);
                check(scene.Render(font,"Una\nDos\nTres\nCuatro",new MovePreviewExample("Destructor",0,"Normal",1,40,100,35)).Status.Contains("supera"),"Gen6 extra lines overflow vertically "+i);
                reject(()=>scene.Render(font,"Texto",new MovePreviewExample("Destructor",18,"Inválido",1,40,100,35)),"Gen6 invalid move type is rejected "+i);
                check(cardHash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(cardPath))),"Gen6 original move-card assets remain unchanged "+i);
            }
            byte[] damaged=(byte[])original.Clone();int magic=original.AsSpan().IndexOf(generation==7?"FFNT"u8:"CFNT"u8);BitConverter.GetBytes(int.MaxValue).CopyTo(damaged,magic+12);reject(()=>GameDescriptionFont.FromArchive(damaged,"fixture"),"out-of-bounds font length rejected "+i);
            check(hash==Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),"font preview never changes original game resource "+i);
        }
        reject(()=>GameMoveDescription6Scene.FromPackage(new byte[40]),"Gen6 truncated move resource is rejected before rendering");
        if(roots.Length==4)
        {
            foreach(var pair in new[]{("PokemonY",false),("PokemonAS",true)})
            {
                string rom=Path.Combine(Path.GetDirectoryName(roots[0])!,pair.Item1,"ExtractedRomFS");
                var font=GameDescriptionFont.Load(rom,6,pair.Item2);var scene=GameMoveDescription6Scene.Load(rom);
                var card=scene.Render(font,"Protege al usuario.\nSi recibe contacto, baja 1 nivel\nel Ataque del rival.",new MovePreviewExample("Escudo Real",8,"Acero",0,0,0,10));
                check(card.Image.IsFrozen&&card.Status.Contains("cabe"),pair.Item1+" native card and Spanish font support the same three-line description");
            }
        }
        if(roots.Length==4)
        {
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
            for(int index=0;index<2;index++)
            {
                using var fixture=new Fixture(index==0?pk3DS.Core.GameVersion.XY:pk3DS.Core.GameVersion.ORAS);ModuleChecks.CreateArchives(fixture);
                foreach(string resource in new[]{index==0?"a/1/8/5":"a/1/6/7","a/0/0/5","a/0/0/0"})
                {
                    string target=Path.Combine(fixture.Game.RomFS,resource);Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(roots[index],"ExtractedRomFS",resource),target,true);
                }
                var window=new TemplateDescriptionWindow("Primera línea",fixture.Session,new MovePreviewExample("Escudo Real",8,"Acero",0,0,0,10));
                var gen6PreviewTask=(Task)typeof(TemplateDescriptionWindow).GetMethod("PreviewAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,null)!;
                while(!gen6PreviewTask.IsCompleted)System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);gen6PreviewTask.GetAwaiter().GetResult();
                var picture=(System.Windows.Controls.Image)typeof(TemplateDescriptionWindow).GetField("_previewImage",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;
                check(picture.Source is BitmapSource {PixelHeight:240}&&window.Description=="Primera línea","Gen6 preview button loads real assets without saving the draft "+index);
                var editor=(System.Windows.Controls.TextBox)typeof(TemplateDescriptionWindow).GetField("_editor",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(window)!;var before=picture.Source;editor.Text="Primera línea\nSegunda línea";
                check(!ReferenceEquals(before,picture.Source)&&window.Description.Contains('\n'),"Gen6 preview updates in place and preserves explicit line breaks "+index);window.Close();
            }
        }
        using var f=new Fixture(pk3DS.Core.GameVersion.USUM);ModuleChecks.CreateArchives(f);
        var dialog=new TemplateDescriptionWindow("Texto sin modificar",f.Session,new MovePreviewExample("Escudo Real",8,"Acero",0,0,0,10));
        var preview=(System.Windows.Controls.Button)typeof(TemplateDescriptionWindow).GetField("_previewButton",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;
        check(preview.IsEnabled&&preview.Content.ToString()=="Previsualizar","description dialog exposes independent preview button");
        SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        var task=(Task)typeof(TemplateDescriptionWindow).GetMethod("PreviewAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,null)!;
        while(!task.IsCompleted)System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);
        task.GetAwaiter().GetResult();
        check(dialog.Description=="Texto sin modificar"&&preview.IsEnabled,"missing game font does not modify text or disable editor");
        if(roots.Length==4)
        {
            string target=Path.Combine(f.Game.RomFS,"a/0/2/5");Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(Path.Combine(roots[3],"ExtractedRomFS/a/0/2/5"),target,true);
            string sceneTarget=Path.Combine(f.Game.RomFS,"a/1/3/2");Directory.CreateDirectory(Path.GetDirectoryName(sceneTarget)!);File.Copy(Path.Combine(roots[3],"ExtractedRomFS/a/1/3/2"),sceneTarget,true);
            task=(Task)typeof(TemplateDescriptionWindow).GetMethod("PreviewAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(dialog,null)!;
            while(!task.IsCompleted)System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.Background);task.GetAwaiter().GetResult();
            var image=(System.Windows.Controls.Image)typeof(TemplateDescriptionWindow).GetField("_previewImage",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;
            check(image.Source is BitmapSource {PixelHeight:240}&&dialog.Description=="Texto sin modificar","preview button renders source font without committing text");
            var old=image.Source;var editor=(System.Windows.Controls.TextBox)typeof(TemplateDescriptionWindow).GetField("_editor",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(dialog)!;editor.Text="Descripción nueva\nOtra línea";
            check(!ReferenceEquals(old,image.Source)&&dialog.Description=="Descripción nueva\nOtra línea","visible preview refreshes while editing without losing multiline draft");
        }
        dialog.Close();
    }
}
