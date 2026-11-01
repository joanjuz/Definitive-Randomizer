"""ARM verification of the integrated C# payload. Reads copies only; no ROM/save writing.
Inventory, diary and UI imports are controlled substitutes. Message routing is checked at
its native entry point, not through an interactive game or emulator desktop.
"""
from pathlib import Path
import argparse,hashlib,json,struct,sys
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--shop',type=Path,required=True);p.add_argument('--registry',type=Path,required=True)
p.add_argument('--legacy-save',type=Path);p.add_argument('--code',type=Path,required=True);p.add_argument('--dependencies',type=Path);p.add_argument('--output',type=Path,required=True)
a=p.parse_args()
if a.dependencies:sys.path.insert(0,str(a.dependencies))
from unicorn import Uc,UC_ARCH_ARM,UC_MODE_ARM,UC_HOOK_CODE
from unicorn.arm_const import *
source=a.shop.read_bytes();code=a.code.read_bytes();registry=json.loads(a.registry.read_text(encoding='utf-8-sig'));entry=registry['Location']
assert hashlib.sha256(code).hexdigest().upper() in {'B5388F7500D91BE01499A99CA007C98212068608ED7C83C43952E1D5148E9E09','D2E9B4D91D7B14BCEF0C34D869C1DA60EF85D98B1DF3C34CBC42E42F54CFBA27'}
assert source[entry+0x800:entry+0x810]==b'DRS-USUM-STOCK1!'
BASE=0x1000000;MAIN=0x100000;CTX=0x2000000;PARAM=CTX+0x1000;ROWS=CTX+0x2000;FIELD=CTX+0x3000;STACK=0x3000000;END=0x4000000
u32=lambda data,at:struct.unpack_from('<I',data,at)[0]
class Machine:
 def __init__(self):
  self.uc=Uc(UC_ARCH_ARM,UC_MODE_ARM)
  for address,size in [(BASE,(len(source)+0xfff)&~0xfff),(MAIN,0x600000),(CTX,0x10000),(STACK,0x10000),(END,0x1000)]:self.uc.mem_map(address,size)
  self.uc.mem_write(BASE,source);self.uc.mem_write(MAIN,code)
  seg=u32(source,0xc8);segments=[u32(source,seg+i*12) for i in range(5)]
  off,n=struct.unpack_from('<II',source,0x128)
  for i in range(n):
   encoded,kind,addend=struct.unpack_from('<III',source,off+i*12)
   if kind&255==2:self.put(BASE+segments[encoded&15]+(encoded>>4),BASE+segments[(kind>>8)&255]+addend)
  self.put(BASE+0x37c,0x4bb1f0);self.stubs={};off,n=struct.unpack_from('<II',source,0x100)
  for i in range(n):
   name,rel=struct.unpack_from('<II',source,off+i*8);encoded=u32(source,rel)
   self.stubs[BASE+0x180+(encoded>>4)-4]=source[name:source.index(0,name)].decode()
  self.uc.hook_add(UC_HOOK_CODE,self.hook);self.reset()
 def put(self,at,value,fmt='<I'):self.uc.mem_write(at,struct.pack(fmt,value))
 def get(self,at,fmt='<I'):return struct.unpack(fmt,self.uc.mem_read(at,struct.calcsize(fmt)))[0]
 def reset(self):
  self.bag={};self.fail_add=False;self.uc.mem_write(FIELD,bytes(0x1000));self.context(8,101)
 def context(self,shop,item,quantity=1,money=1000,mode=0):
  self.put(CTX+0x20,PARAM);self.put(PARAM,mode);self.put(PARAM+4,shop)
  self.put(PARAM+0x18,20);self.put(PARAM+0x1c,19);self.put(PARAM+0x20,0)
  self.put(CTX+0x6c,ROWS);self.put(ROWS,item);self.put(ROWS+4,200);self.put(ROWS+12,item);self.put(ROWS+16,200)
  self.put(CTX+0x70,1);self.put(CTX+0x82,0,'<h');self.put(CTX+0x84,quantity,'<h');self.put(CTX+0x86,0,'<h');self.put(CTX+0x74,money);self.put(CTX+0x38,FIELD)
 def hook(self,uc,at,size,user):
  if at==END:uc.emu_stop();return
  if at not in self.stubs or at==BASE+0x378:return
  name=self.stubs[at];r1=uc.reg_read(UC_ARM_REG_R1);r2=uc.reg_read(UC_ARM_REG_R2)
  if 'MyItem10GetItemNum' in name:uc.reg_write(UC_ARM_REG_R0,self.bag.get(r1,0))
  elif 'ITEM_CheckWazaMachine' in name:uc.reg_write(UC_ARM_REG_R0,0)
  elif 'MyItem3Add' in name:
   if self.fail_add:uc.reg_write(UC_ARM_REG_R0,0)
   else:self.bag[r1]=self.bag.get(r1,0)+r2;uc.reg_write(UC_ARM_REG_R0,1)
  elif name=='__aeabi_uidivmod':
   value=uc.reg_read(UC_ARM_REG_R0);uc.reg_write(UC_ARM_REG_R0,value//r1 if r1 else 0);uc.reg_write(UC_ARM_REG_R1,value%r1 if r1 else 0)
  uc.reg_write(UC_ARM_REG_PC,uc.reg_read(UC_ARM_REG_LR))
 def run(self,at,stop=END):
  self.uc.reg_write(UC_ARM_REG_SP,STACK+0xff00);self.uc.reg_write(UC_ARM_REG_LR,END);self.uc.emu_start(BASE+at,stop,count=100000);return self.uc.reg_read(UC_ARM_REG_R0)
 def flags(self):return bytes(self.uc.mem_read(FIELD,0x1000))
 def preflight(self):self.uc.reg_write(UC_ARM_REG_R0,CTX);return self.run(0x276c)
 def confirm(self):
  self.uc.reg_write(UC_ARM_REG_R4,CTX);self.uc.reg_write(UC_ARM_REG_R6,0);stopped=[]
  def stop(uc,at,size,user):
   if at in (BASE+0x158c,BASE+0x1650):stopped.append(at);uc.emu_stop()
  hook=self.uc.hook_add(UC_HOOK_CODE,stop)
  try:self.run(0x15e4)
  finally:self.uc.hook_del(hook)
  assert stopped,'Confirmation left expected path'
  if stopped[-1]==BASE+0x158c:return False
  self.run(0x1694,BASE+0x16c0);return True
 def message(self):
  result=[]
  def stop(uc,at,size,user):
   if at==BASE+0x1f44:result.append((uc.reg_read(UC_ARM_REG_R0),uc.reg_read(UC_ARM_REG_R1),uc.reg_read(UC_ARM_REG_R2),uc.reg_read(UC_ARM_REG_R3)));uc.emu_stop()
  hook=self.uc.hook_add(UC_HOOK_CODE,stop);self.uc.reg_write(UC_ARM_REG_R4,CTX)
  try:self.run(0x3ca4)
  finally:self.uc.hook_del(hook)
  assert len(result)==1;return result[0]
 def message_lifecycle(self):
  ui=CTX+0x5000;vtable=CTX+0x5100;close=END+0x100
  self.put(CTX+0x64,ui);self.put(ui,vtable);self.put(vtable+0x54,close)
  self.uc.mem_write(close,bytes.fromhex('1eff2fe1'));events=[]
  def dispatch(uc,at,size,user):
   if at==BASE+0x490:events.append(('text',uc.reg_read(UC_ARM_REG_R1)))
   if at==close:events.append(('close',0))
   if at in (BASE+0x4e8,BASE+0x4f0):
    uc.reg_write(UC_ARM_REG_R0,6 if at==BASE+0x4e8 else 0)
    uc.reg_write(UC_ARM_REG_PC,uc.reg_read(UC_ARM_REG_LR))
   if at in (BASE+0x2030,BASE+0x2010):uc.reg_write(UC_ARM_REG_PC,uc.reg_read(UC_ARM_REG_LR))
  hook=self.uc.hook_add(UC_HOOK_CODE,dispatch)
  try:
   self.uc.reg_write(UC_ARM_REG_R4,CTX);self.uc.reg_write(UC_ARM_REG_R0,4)
   self.run(entry+0x700,BASE+0x3d18)
   check(self.get(CTX+4)==3 and events==[('text',registry['MessageId'])],'exhausted_message_enters_native_dialog_with_custom_text')
   for frame in range(3):self.uc.reg_write(UC_ARM_REG_R0,CTX);self.run(0x2b08)
   return events,self.get(CTX+4),self.get(CTX+0x8c,'<H')
  finally:self.uc.hook_del(hook)
checks=[]
def check(ok,name):
 if not ok:raise AssertionError(name)
 checks.append(name);print('PASS',name)
m=Machine();offers=registry['Offers']
# White list: registered rows the shop never finds (shop field with bit 15); they must stay unlimited.
exempt={(o['Shop'],o['Item']) for o in registry.get('Exempt',[])};offers=[o for o in offers if (o['Shop'],o['Item']) not in exempt]
for offer in offers:
 shop,item=offer['Shop'],offer['Item'];m.reset();m.context(shop,item);before=m.flags()
 check(m.preflight()==0 and m.get(CTX+0x86,'<h')==1 and m.flags()==before,f'all_offers_available_without_consumption_shop_{shop}_item_{item}')
 check(m.confirm() and m.bag.get(item)==1 and m.get(CTX+0x74)==800 and m.preflight()==4,f'all_offers_exactly_once_shop_{shop}_item_{item}')
 check(m.message()==(CTX,registry['MessageId'],0,1),f'all_offers_dedicated_message_shop_{shop}_item_{item}')
shared=next(o for o in offers if any(p['Item']==o['Item'] and p['Shop']!=o['Shop'] for p in offers) and any(p['Shop']==o['Shop'] and p['Item']!=o['Item'] for p in offers));shop,item=shared['Shop'],shared['Item'];m.reset();m.context(shop,item);m.confirm();saved=m.flags();money=m.get(CTX+0x74);bag=dict(m.bag)
check(not m.confirm() and m.flags()==saved and m.get(CTX+0x74)==money and m.bag==bag,'second_confirmation_changes_neither_money_nor_inventory_nor_stock')
m.bag.clear();check(m.preflight()==4,'selling_or_using_item_does_not_restore_stock')
m.reset();m.uc.mem_write(FIELD,saved);m.context(shop,item);check(m.preflight()==4,'serialized_memory_reload_keeps_stock_not_real_game_save')
other=next(o for o in offers if o['Shop']!=shop and o['Item']==item);m.context(other['Shop'],item);check(m.preflight()==0 and m.confirm(),'same_item_in_another_special_shop_has_independent_stock')
other=next(o for o in offers if o['Shop']==shop and o['Item']!=item);m.context(shop,other['Item']);check(m.preflight()==0 and m.confirm(),'different_item_in_same_shop_has_independent_stock')
m.context(shop,item);m.put(CTX+0x70,2);m.put(CTX+0x82,1,'<h');check(m.preflight()==4,'duplicate_rows_share_shop_item_stock')
for qty in [-32768,-1,0,2,99,32767]:
 m.reset();m.context(shop,item,quantity=qty);check(m.confirm() and m.bag.get(item)==1 and m.get(CTX+0x74)==800,'quantity_clamped_'+str(qty).replace('-','minus_'))
for name,money,bag,fail,message in [('insufficient_money',199,0,False,19),('full_bag',1000,999,False,20),('delivery_failure',1000,0,True,None)]:
 m.reset();m.context(shop,item,money=money);m.bag[item]=bag;m.fail_add=fail;before=m.flags();old=dict(m.bag)
 check(not m.confirm() and m.flags()==before and m.bag==old and m.get(CTX+0x74)==money,name+'_preserves_stock_money_and_inventory')
 if message is not None:check(m.message()[1]==message,name+'_keeps_original_message_route')
for ordinary in range(8):
 m.reset();m.context(ordinary,item,quantity=2);before=m.flags();check(m.preflight()==0 and m.get(CTX+0x86,'<h')>1 and m.confirm() and m.bag[item]==2 and m.flags()==before,f'ordinary_progression_shop_{ordinary}_unlimited')
for mode in range(1,5):
 m.reset();m.context(shop,item,mode=mode);m.uc.reg_write(UC_ARM_REG_R0,CTX);check(m.run(entry+0x300)==0,'bp_and_other_native_modes_excluded_'+str(mode))
for bit in [1,8,9,64,65,1984]:
 m.reset();m.uc.reg_write(UC_ARM_REG_R0,FIELD);m.uc.reg_write(UC_ARM_REG_R1,bit);check(m.run(entry+0x500)==0,'bitmap_boundary_initial_'+str(bit));m.uc.reg_write(UC_ARM_REG_R0,FIELD);m.uc.reg_write(UC_ARM_REG_R1,bit);m.run(entry+0x600)
 expected=0xd0c+((bit-1)>>3);after=m.flags();check(after[expected]==1<<((bit-1)&7) and all(after[i]==0 for i in range(0x1000) if not 0xd04<=i<0xd0c and i!=expected),'bitmap_boundary_only_owned_bit_'+str(bit))
 m.uc.reg_write(UC_ARM_REG_R0,FIELD);m.uc.reg_write(UC_ARM_REG_R1,bit);check(m.run(entry+0x500)==1,'bitmap_boundary_purchased_'+str(bit))
for bit in [0,1985,65535]:
 m.reset();before=m.flags();m.uc.reg_write(UC_ARM_REG_R0,FIELD);m.uc.reg_write(UC_ARM_REG_R1,bit);check(m.run(entry+0x500)==1 and m.flags()==before,'bitmap_invalid_index_fails_closed_'+str(bit))
for name,offset,value in [('foreign_magic',0xd04,0x12345678),('different_identity',0xd08,1),('orphan_bit_at_end',0xe00,1)]:
 m.reset();m.context(shop,item);m.put(FIELD+offset,value);before=m.flags();check(m.preflight()==4 and not m.confirm() and m.flags()==before and not m.bag and m.get(CTX+0x74)==1000,name+'_not_overwritten_or_charged')
m.reset();m.context(shop,item);before=m.flags();m.uc.reg_write(UC_ARM_REG_R0,CTX);m.uc.reg_write(UC_ARM_REG_R1,1);m.run(0x1560,BASE+0x158c);check(m.flags()==before and not m.bag,'cancel_keeps_stock_and_inventory')
m.reset();m.context(shop,item);m.confirm()
events,state,phase=m.message_lifecycle()
check(events==[('text',registry['MessageId']),('close',0)] and state==1 and phase==0,'exhausted_message_native_completion_returns_to_shop_without_loop')
prototype=[(10,101),(11,101),(10,102)]
for mask in range(8):
 m.reset();m.put(FIELD+0xd04,0x44525331);m.put(FIELD+0xd08,0x53544f43);m.put(FIELD+0xd0c,mask);before=m.flags()
 for bit,key in enumerate(prototype):
  m.context(*key);check(m.preflight()==(4 if mask&(1<<bit) and key not in exempt else 0) and m.flags()==before,f'prototype_mask_{mask}_key_{bit}_read_only_exact_status')
 unrelated=next(o for o in offers if (o['Shop'],o['Item']) not in prototype)
 m.context(unrelated['Shop'],unrelated['Item']);check(m.preflight()==0 and m.confirm(),f'prototype_mask_{mask}_unbought_item_purchasable')
 check(m.get(FIELD+0xd04)==0x44525332 and m.get(FIELD+0xd08)==registry['Identity'],f'prototype_mask_{mask}_migrates_on_successful_purchase')
 for bit,key in enumerate(prototype):
  m.context(*key);check(m.preflight()==(4 if mask&(1<<bit) and key not in exempt else 0),f'prototype_mask_{mask}_preserves_purchase_{bit}_after_migration')
 check(all(m.flags()[i]==before[i] for i in range(0x1000) if not 0xd04<=i<0xe04),f'prototype_mask_{mask}_does_not_modify_other_save_bytes')
for name,offset,value in [('foreign_prototype_identity',0xd08,1),('unknown_prototype_bit',0xd0c,8),('occupied_prototype_tail',0xd10,1)]:
 m.reset();m.put(FIELD+0xd04,0x44525331);m.put(FIELD+0xd08,0x53544f43);m.put(FIELD+offset,value);m.context(shop,item);before=m.flags()
 check(m.preflight()==4 and not m.confirm() and m.flags()==before,name+'_rejects_without_overwriting')
if a.legacy_save:
 save=a.legacy_save.read_bytes();header=struct.pack('<II',0x44525331,0x53544f43);at=save.find(header)
 assert at>=0 and save.find(header,at+1)<0,'Expected one known prototype header'
 m.reset();m.uc.mem_write(FIELD+0xd04,save[at:at+256]);before=m.flags();m.context(10,234)
 check(m.preflight()==0 and m.flags()==before,'real_prototype_save_leftovers_first_purchase_allowed_without_save_write')
 check(m.confirm() and m.bag.get(234)==1 and m.preflight()==4,'real_prototype_save_leftovers_purchase_then_exhaustion')
 check(m.message_lifecycle()[1:]==(1,0),'real_prototype_save_exhausted_dialog_returns_to_shop')
 check(a.legacy_save.read_bytes()==save,'real_prototype_save_source_unchanged')
for shop,item in sorted(exempt):
 m.reset();m.context(shop,item);before=m.flags()
 check(m.preflight()==0 and m.confirm() and m.preflight()==0 and m.confirm() and m.bag.get(item)==2 and m.flags()==before,f'whitelist_item_unlimited_without_save_bits_shop_{shop}_item_{item}')
check(a.shop.read_bytes()==source,'input_cro_unchanged')
a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps({'Checks':checks,'Count':len(checks),'ShopSha256':hashlib.sha256(source).hexdigest(),'Offers':len(offers),'Scope':'ARM only: imports stubbed, message entry routing, synthetic save memory; no actual game UI/save'},indent=2),encoding='utf-8')
print('Completed',len(checks),'integrated ARM checks.')
