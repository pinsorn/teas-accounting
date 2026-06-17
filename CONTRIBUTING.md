# การร่วมพัฒนา (Contributing)

ยินดีต้อนรับทุกคนมาช่วยกันพัฒนา 🙌 ไม่ว่าจะเป็น bug fix, feature ใหม่, เอกสาร, หรือไอเดีย — เปิดรับหมด!
โปรเจกต์อยู่ภายใต้ [GNU AGPL-3.0](LICENSE) (โอเพนซอร์ส) ทุก contribution ถือว่าอยู่ภายใต้ license เดียวกัน

## Developer Certificate of Origin (DCO)

ทุก commit ต้อง **sign-off** เพื่อรับรองว่าคุณมีสิทธิ์ส่งโค้ดนั้น (ตาม [DCO 1.1](https://developercertificate.org/)):

```bash
git commit -s -m "your message"
```

จะเพิ่มบรรทัด `Signed-off-by: Your Name <you@example.com>` ในข้อความ commit

## ขั้นตอน

1. Fork repo แล้วสร้าง branch จาก `main`
2. แก้ไข + เพิ่ม / อัปเดต test ให้ผ่าน
3. รัน gate ก่อนเปิด PR:
   - Backend: `dotnet test backend/Accounting.sln` (ต้องมี PostgreSQL — ดู `README.md`)
   - Frontend: `cd frontend && pnpm exec tsc --noEmit`
4. เปิด Pull Request ไป `main` พร้อมอธิบายการเปลี่ยนแปลง (commit แบบ Conventional Commits)

## แนวทาง

- โค้ด / comment / commit เป็นภาษาอังกฤษ; ข้อความ user-facing เป็นไทย (หลัก) + อังกฤษ ผ่าน i18n
- อ่าน `CLAUDE.md` (engineering conventions) และ `docs/accounting-system-plan.md` (as-built spec) ก่อนเริ่ม
- กฎ compliance ภาษีห้ามพลาด (ดู `CLAUDE.md` §4) — เอกสารที่ post แล้วแก้ไม่ได้, เลขเอกสารห้ามขาดช่วง,
  multi-tenant `company_id` ทุก query
