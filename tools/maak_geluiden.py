# Acht korte timergeluiden, zelf berekend (geen opnames van anderen): 22050 Hz, mono, 16 bit
import numpy as np, wave, sys, os
SR = 22050
out = sys.argv[1]
def t(d): return np.arange(int(SR*d))/SR
def env(x, a=0.005, decay=3.0):
    n=len(x); tt=np.arange(n)/SR
    e=np.minimum(1, tt/a) * np.exp(-tt*decay)
    return x*e
def bell(f, d=1.8, decay=2.5, parts=((1,1),(2.0,0.5),(2.76,0.35),(5.4,0.15),(8.93,0.08))):
    tt=t(d); x=np.zeros_like(tt)
    for k,a in parts: x += a*np.sin(2*np.pi*f*k*tt)*np.exp(-tt*decay*(0.6+k*0.25))
    return env(x, 0.003, 0.2)
def tone(f, d, decay=6, harm=((1,1),(2,0.3),(3,0.12))):
    tt=t(d); x=sum(a*np.sin(2*np.pi*f*k*tt) for k,a in harm)
    return env(x,0.004,decay)
def place(total, items):
    y=np.zeros(int(SR*total))
    for start, s in items:
        i=int(SR*start); j=min(len(y), i+len(s)); y[i:j]+=s[:j-i]
    return y
def save(name, y):
    y = y/ (np.max(np.abs(y))+1e-9) * 0.85
    fade=int(SR*0.05); y[-fade:]*=np.linspace(1,0,fade)
    d=(y*32767).astype(np.int16)
    with wave.open(os.path.join(out,name+'.wav'),'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(SR); w.writeframes(d.tobytes())
C5,E5,G5,C6,A4,D5 = 523.25,659.25,783.99,1046.5,440.0,587.33
# 1 belletje: een heldere bel
save('1-belletje', place(2.0, [(0, bell(1318.5, 1.9, 2.2))]))
# 2 wekker: snel piepen, vier keer twee piepjes
beep=tone(2000,0.09,2,((1,1),(3,0.2)))
save('2-wekker', place(2.0, [(i*0.45+k*0.14, beep) for i in range(4) for k in range(2)]))
# 3 melding: twee tonen omhoog
save('3-melding', place(1.6, [(0, tone(E5*2,1.0,4)), (0.16, tone(C6*1.5,1.2,3.5))]))
# 4 gong: diep en zacht
g=t(2.0); gong=sum(a*np.sin(2*np.pi*f*g+0.3*np.sin(2*np.pi*2.2*g)) for f,a in ((110,1),(176,0.6),(262,0.35),(415,0.2)))*np.exp(-g*1.6)
save('4-gong', env(gong,0.02,0.1))
# 5 marimba: drie noten omhoog
mar=lambda f: tone(f,0.6,9,((1,1),(4,0.25),(10,0.05)))
save('5-marimba', place(1.4, [(0,mar(C5)),(0.15,mar(E5)),(0.30,mar(G5)),(0.45,mar(C6))]))
# 6 schoolbel: snel tikkende bel, 1,5 seconde
sb=bell(1568,0.12,20)
save('6-schoolbel', place(1.8, [(i*0.06, sb*(1-i/30)) for i in range(25)]))
# 7 glazen: twee zachte tikjes op een glas
gl=bell(2093,1.2,3.2,((1,1),(2.32,0.4),(4.25,0.15)))
save('7-glazen', place(1.8, [(0,gl),(0.35,gl*0.8)]))
# 8 harp: een rustig akkoord dat oploopt
hp=lambda f: tone(f,1.4,2.4,((1,1),(2,0.35),(3,0.15),(4,0.07)))
save('8-harp', place(2.0, [(i*0.09, hp(f)) for i,f in enumerate((C5,E5,G5,C6,E5*2))]))
print("klaar")
