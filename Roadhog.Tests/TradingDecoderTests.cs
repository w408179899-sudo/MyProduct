using System.Text;
using Roadhog.Core.Model;

internal static partial class PersonalShopDecoderTests
{
    private static void Grid(Fixture m, ulong address, ulong vector, ulong item)
    {
        m.I(address+0x2E0,1); m.I(address+0x2E8,1); m.D(address+0x2F0,32); m.D(address+0x2F8,32);
        m.U(address+0x368,vector);m.U(address+0x370,vector+8);m.U(vector,item);
    }
    public static Task TradingPurchaseDecodeAsync()
    {
        var m=new Fixture();
        const ulong window=0x40000000,container=0x41000000,list=0x42000000,basket=0x43000000,
            buy=0x44000000,manager=0x45000000,head=0x46000000,node=0x47000000,record=0x48000000,
            modal=0x49000000,count=0x4A000000,ok=0x4B000000;
        m.U(Fixture.Game+0xD63990+134*8,window);
        m.Widget(window,"shop_dialog",100,100,300,400,0,0);
        m.Widget(container,"ps_shop_buy_container",0,0,300,400,0,0);
        m.Widget(list,"item_list",10,40,100,100,0,0);m.Widget(basket,"item_basket",120,40,100,100,0,0);
        m.Widget(buy,"ok",10,250,60,20,0,0);m.Children(window,container);m.Children(container,list,basket,buy);
        m.I(window+1252,3);m.I(window+1240,700);m.U(window+1288,list);m.U(window+1344,basket);
        Grid(m,list,Fixture.Vector,Fixture.Item);m.U(basket+0x368,0);m.U(basket+0x370,0);
        m.U(Fixture.Game+0xD4B010,manager);m.U(manager+2368,head);m.U(head+8,node);m.I(node+32,123);m.U(node+40,record);
        m.I(record+8,123);m.I(record+12,567);m.U(record+128,35);
        var ui=m.Decoder().Read(Fixture.Game).Purchase;
        Require(ui is{IsOpen:true,SellerObjectId:700,HoveredInstanceId:123}&&ui.Items[0].UnitPrice==35&&ui.Items[0].Quantity==26&&ui.Items[0].Point==new GameUiPoint(126,156),"purchase ownership, ordered stock, price, grid and hover");
        m.U(window+2248,modal);m.I(window+2236,123);
        m.Widget(modal,"item_count_dialog",200,200,200,100,0,0);m.Widget(count,"count",10,10,60,20,0,0);m.Widget(ok,"ok",10,50,60,20,0,0);m.Children(modal,count,ok);
        m.U(modal+1504,3);m.U(modal+1520,26);m.U(modal+1544,count);
        ui=m.Decoder().Read(Fixture.Game).Purchase;
        Require(ui.QuantityDialog is{InstanceId:123,Quantity:3,Maximum:26}&&ui.BuyButton==null&&ui.QuantityDialog.Confirm!=null,"quantity modal is bound and final purchase is blocked until basket");
        m.I(window+2236,999);Reject(()=>m.Decoder().Read(Fixture.Game),"foreign modal identity cannot publish");m.I(window+2236,123);
        m.ShortAddress=record+128;Reject(()=>m.Decoder().Read(Fixture.Game),"partial unit price cannot publish zero");m.ShortAddress=null;
        m.I(record+12,999);Reject(()=>m.Decoder().Read(Fixture.Game),"map and display identity must agree");m.I(record+12,567);
        // Real remote-shop keys start at zero, unlike local inventory instance IDs.
        m.U(Fixture.Game+0xD63ED0,0);m.I(Fixture.Item+160,0);m.I(node+32,0);m.I(record+8,0);m.I(window+2236,0);
        ui=m.Decoder().Read(Fixture.Game).Purchase;
        Require(ui.Items.Single().InstanceId==0&&ui.HoveredInstanceId==0&&ui.QuantityDialog?.InstanceId==0,"zero remote key binds stock, hover and quantity dialog");
        m.I(list+0x3F0,ushort.MaxValue);
        Require(m.Decoder().Read(Fixture.Game).Purchase.HoveredInstanceId==null,"no hover cannot masquerade as remote item zero");
        m.I(list+0x3F0,0);m.I(record+8,1);Reject(()=>m.Decoder().Read(Fixture.Game),"zero remote key still requires matching record identity");m.I(record+8,0);
        m.U(window+2248,0);
        const ulong basketVector=0x54000000,basketItem=0x55000000;
        Grid(m,basket,basketVector,basketItem);m.I(basketItem+160,0);m.I(basketItem+168,567);m.U(basketItem+176,26);
        m.U(Fixture.Item+176,0);
        ui=m.Decoder().Read(Fixture.Game).Purchase;
        Require(ui.Items.Single() is {Quantity:0,Point:null}&&ui.HoveredInstanceId==null,"full basket retains zero stock without an actionable hover");
        Require(ui.Basket.Single() is {InstanceId:0,Quantity:26,Point:not null}&&ui.HoveredBasketInstanceId==0,"full basket stays readable and exposes a verified cancellation target");
        m.I(basket+0x3F0,ushort.MaxValue);
        Require(m.Decoder().Read(Fixture.Game).Purchase.HoveredBasketInstanceId==null,"basket key zero does not imply hover");m.I(basket+0x3F0,0);
        m.U(basketItem+176,0);Reject(()=>m.Decoder().Read(Fixture.Game),"empty basket row is not valid stock");m.U(basketItem+176,26);
        m.I(record+12,999);Reject(()=>m.Decoder().Read(Fixture.Game),"zero stock still validates template identity");m.I(record+12,567);
        m.U(Fixture.Game+0xD63990+336*8,modal);
        ui=m.Decoder().Read(Fixture.Game).Purchase;
        Require(ui.Basket.Single().Point==null&&ui.HoveredBasketInstanceId==null&&ui.BuyButton==null,"foreign modal masks basket cancellation and purchase");
        m.U(Fixture.Game+0xD63990+336*8,0);
        m.U(window+0x28,0);Require(!m.Decoder().Read(Fixture.Game).Purchase.IsOpen,"closed replaces previous remote shop");
        return Task.CompletedTask;
    }

    public static Task AuctionListingsDecodeAsync()
    {
        var m=new Fixture();const ulong window=0x40000000,tab=0x41000000,list=0x42000000,
            editor=0x43000000,input=0x44000000,price=0x45000000,confirm=0x46000000,cancel=0x47000000,
            minimum=0x48000000,modal=0x49000000,yes=0x4A000000;
        m.U(Fixture.Game+0xD63990+152*8,window);m.Widget(window,"vendor_dialog",100,100,700,500,0,0);
        m.Widget(tab,"tab",0,0,300,30,0,0);m.I(tab+0x2F8,1);m.U(window+1256,tab);
        m.Widget(list,"own_list",10,40,500,400,0,0);m.Children(window,tab,list);m.U(window+1384,list);m.I(window+1432,1);
        Grid(m,list,Fixture.Vector,Fixture.Item);m.U(Fixture.Item+288,260);m.U(Fixture.Item+296,7);
        const ulong columns=0x50000000,runs=0x51000000;
        m.U(Fixture.Item+72,columns);m.U(Fixture.Item+80,columns+3*24);
        void Wide(ulong a,string text)
        {
            Require(text.Length<8,"test inline text");m.Put(a,Encoding.Unicode.GetBytes(text));m.U(a+16,(ulong)text.Length);m.U(a+24,7);
        }
        for(var i=0;i<3;i++){var c=columns+(ulong)i*24;var r=runs+(ulong)i*80;m.U(c,r);m.U(c+8,r+80);Wide(r,new[]{"item","260","7 days"}[i]);}
        var ui=m.Decoder().ReadAuction(Fixture.Game);
        Require(ui.ListingsLoaded&&ui.ListingCapacity==15&&ui.Listings.Single() is{ListingId:123,TemplateId:567,Quantity:26,TotalPrice:260,TimeText:"7 days"},"own listing identity, count and displayed time");
        m.I(list+736,0);m.I(list+1316,10);m.I(list+1320,0);m.I(list+1324,1);m.D(list+768,24);m.U(list+896,0x60000000);m.U(list+904,0x60000008);
        Require(m.Decoder().ReadAuction(Fixture.Game).Listings[0].Point==new GameUiPoint(126,180),"list mode includes header instead of using bag-grid columns");
        m.I(list+1320,1);Require(m.Decoder().ReadAuction(Fixture.Game).Listings[0].Point==null,"off-page listing has no click target");m.I(list+1320,0);
        m.D(list+824,-50);Require(m.Decoder().ReadAuction(Fixture.Game).Listings[0].Point==null,"scrolled-out listing cannot be clicked outside content");m.D(list+824,0);
        m.U(Fixture.Game+0xD63990+310*8,editor);m.Widget(editor,"vendor_sell_stack_dialog",200,200,300,200,0,0);
        m.Widget(input,"count",10,10,60,20,0,0);m.Widget(price,"price",10,40,60,20,0,0);m.Widget(confirm,"ok",10,90,60,20,0,0);m.Widget(cancel,"cancel",90,90,60,20,0,0);
        m.Children(editor,input,price,confirm,cancel);m.U(editor+1240,list);m.U(editor+1248,input);m.U(editor+1256,price);m.U(editor+1352,confirm);
        m.U(editor+1360,26);m.U(editor+1368,26);m.U(editor+1376,100);m.U(editor+1384,10);m.I(editor+1608,1);m.I(editor+1616,456);
        m.U(editor+1280,minimum);const ulong vtable=0x52000000,getter=0x53000000;
        m.U(minimum,vtable);m.U(vtable+632,getter);m.Put(getter,new byte[]{0x48,0x8D,0x81,0x00,0x03,0,0});Wide(minimum+0x300,"150");
        ui=m.Decoder().ReadAuction(Fixture.Game);
        Require(ui.Editor is{InstanceId:456,MaximumQuantity:26,MinimumAllowedPrice:10,MarketMinimum:150,UnitPriceMode:true}&&ui.Editor.ConfirmButton!=null,"reference minimum differs from price floor; exact inventory identity and controls");
        m.U(confirm+0x28,1);Require(m.Decoder().ReadAuction(Fixture.Game).Editor!.ConfirmButton==null,"disabled submit stays nonclickable");
        m.U(editor+0x28,0);m.U(Fixture.Game+0xD63990+336*8,modal);m.Widget(modal,"msgbox",300,300,200,100,0,0);m.Widget(yes,"",10,50,60,20,0,0);m.Children(modal,yes);m.U(modal+0x578,yes);
        m.I(modal+0x4D8,1);m.I(modal+0x4DC,2108);m.I(modal+0x4E0,2109);m.I(window+1448,0);
        ui=m.Decoder().ReadAuction(Fixture.Game);Require(ui.WithdrawConfirmation is{ListingId:123}&&ui.WithdrawConfirmation.ConfirmButton!=null,"right-click withdrawal binds selected listing and callback");
        Require(ui.Listings.All(l=>l.Point==null)&&ui.HoveredListingId==0,"modal blocks underlying listing clicks");
        m.I(modal+0x4D8,9);Require(m.Decoder().ReadAuction(Fixture.Game).WithdrawConfirmation?.ListingId==123,"parent modal flag preserves withdrawal type");
        m.I(modal+0x4DC,2101);Require(m.Decoder().ReadAuction(Fixture.Game) is{OtherModalOpen:true,WithdrawConfirmation:null},"unrelated confirmation blocked");
        m.ShortAddress=Fixture.Item+288;Reject(()=>m.Decoder().ReadAuction(Fixture.Game),"short listing total cannot publish");
        return Task.CompletedTask;
    }
}
