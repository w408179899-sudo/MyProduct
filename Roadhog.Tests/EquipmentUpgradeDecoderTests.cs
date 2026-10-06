using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Vmm;

internal static partial class PersonalShopDecoderTests
{
    public static Task EquipmentUpgradeAsync()
    {
        var m = new Fixture();
        const ulong editor=0x41000000, target=0x42000000, stone=0x43000000, ok=0x44000000, cancel=0x45000000, close=0x46000000, modal=0x47000000;
        m.U(Fixture.Game+0xD63990+250*8,editor);
        m.Widget(editor,"enchant_option_dialog",100,100,400,300,0,0);
        m.Widget(ok,"ok",10,200,60,20,0,0);m.Widget(cancel,"cancel",80,200,60,20,0,0);m.Widget(close,"cancel",380,0,20,20,0,0);
        m.Children(editor,ok,cancel,close);m.U(editor+1248,ok);m.U(editor+1320,target);m.U(editor+1336,stone);
        m.I(target+952,11);m.I(stone+952,13);
        var initial=m.Decoder().ReadEquipmentUi(Fixture.Game);
        Require(initial.Dialog is {EquipmentId:11,MaterialId:13,FinalConfirmation:false},"editor slots bind exact pair despite duplicate cancel widgets");
        m.U(Fixture.Game+0xD63990+337*8,modal);m.Widget(modal,"",300,200,200,200,0,0);
        m.U(modal+1400,ok);m.U(modal+1408,cancel);m.Children(modal,ok,cancel);m.I(modal+968,250);
        m.I(modal+1240,1);m.I(modal+1244,2101);m.I(modal+1248,2102);
        var confirmed=m.Decoder().ReadEquipmentUi(Fixture.Game);
        Require(confirmed.Dialog is {DialogId:337,FinalConfirmation:true,EquipmentId:11},"owned second modal");
        m.I(modal+968,249);
        Require(m.Decoder().ReadEquipmentUi(Fixture.Game) is {OtherModalOpen:true,Dialog.ConfirmButton:null},"foreign owner is never actionable");
        m.ShortAddress=target+952;Reject(()=>m.Decoder().ReadEquipmentUi(Fixture.Game),"short identity read rejected");
        var store=new DmaStableSnapshotStore(AionVmmSnapshotChannels.Registry);
        var context=new GameApiReadContext("a",1,"Aion.bin","fake");
        var channel=AionVmmSnapshotChannels.EquipmentUpgradeUi;
        store.Resolve("s",channel,context,OperationResult<EquipmentUpgradeUi>.Ok(initial),DateTimeOffset.UtcNow);
        var held=store.Resolve("s",channel,context,OperationResult<EquipmentUpgradeUi>.Fail("short"),DateTimeOffset.UtcNow.AddDays(1));
        Require(held.Result.Value==initial,"failed read holds official publication without TTL");
        var changed=store.Resolve("s",channel,context,OperationResult<EquipmentUpgradeUi>.Ok(confirmed),DateTimeOffset.UtcNow.AddDays(1));
        Require(changed.Result.Value==confirmed,"first valid transition publishes immediately");
        m=new Fixture();
        const ulong manager=0x50000000,head=0x51000000,node=0x52000000,item=0x53000000;
        m.U(Fixture.Game+0xD4B010,manager);m.U(manager+0x780,head);m.U(manager+0x788,1);
        m.U(head+8,node);m.U(node,head);m.U(node+16,head);m.I(node+32,123);m.U(node+40,item);
        m.I(item+8,123);m.I(item+12,113100650);m.U(item+16,1);
        m.Put(item+24,System.Text.Encoding.Unicode.GetBytes("robe"));m.U(item+40,4);m.U(item+48,7);
        m.Put(item+169,new byte[]{9,0,0,0,0,1,104,0,0,0,0,0,0,0,0,0,0,0});
        var inventory=m.Decoder().ReadEquipmentInventory(Fixture.Game,_=>3,out var complete,_=>30);
        Require(complete&&inventory.Items.Single() is {SocketCount:4,UsedSockets:1,EnchantLevel:9,EquipmentLevel:30},"base plus extra sockets and actual IDs");
        var missingLevel=m.Decoder().ReadEquipmentInventory(Fixture.Game,_=>3,out complete,_=>throw new InvalidDataException("missing level"));
        Require(!complete&&missingLevel.Items.Count==0,"missing equipment level cannot publish as lowest priority");
        m.ShortAddress=item+169;
        var partial=m.Decoder().ReadEquipmentInventory(Fixture.Game,_=>3,out complete,_=>30);
        Require(!complete&&partial.Items.Count==0,"short upgrade fields cannot publish defaults");
        var heldInventory=AionVmmGameApi.MergeEquipmentInventory(inventory,partial);
        Require(heldInventory.Items.Single().UsedSockets==1,"partial omission preserves equipment");
        var updated=inventory.Items.Single() with {EnchantLevel=10};
        Require(AionVmmGameApi.MergeEquipmentInventory(inventory,new(new[]{updated})).Items.Single().EnchantLevel==10,"valid partial item updates immediately");
        m.ShortAddress=null;m.I(item+12,166000060);
        var materials=m.Decoder().ReadEquipmentInventory(Fixture.Game,_=>0,out complete,_=>60);
        Require(complete&&materials.Items.Single().EnchantStoneLevel==60,"stone level comes from validated template metadata");
        materials=m.Decoder().ReadEquipmentInventory(Fixture.Game,_=>0,out complete,_=>throw new InvalidDataException("short template"));
        Require(!complete&&materials.Items.Count==0,"failed stone metadata never publishes level zero");
        m.U(head+8,head);m.U(manager+0x788,0);
        Require(m.Decoder().ReadEquipmentInventory(Fixture.Game,_=>3,out complete,_=>30).Items.Count==0&&complete,"proved empty tree prunes");
        return Task.CompletedTask;
    }
}
