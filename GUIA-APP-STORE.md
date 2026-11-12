# Shapester → App Store (sin Mac)

Camino: **Apple Developer → certificado y perfil → App Store Connect → compilación en la nube (Unity Build Automation) → subir → ficha de la tienda → revisión de Apple.**

Leyenda: 🧑 = lo hacés vos (cuentas, pagos, contratos) · 🤖 = lo hago yo.

---

## ✅ Ya hecho (🤖)
- Proyecto configurado para iOS: ID `com.aferrante.shapester`, versión 1.0 (build 1), vertical, iOS 15+, pantalla completa, IL2CPP/ARM64.
- Ícono de 1024×1024 retro (`Assets/Icons/AppIcon.png`).
- Declaración de cifrado automática en cada build (el juego solo usa HTTPS/WSS estándar → Apple no pide papeles de exportación).
- Menú **Shapester → Configure iOS** para re-aplicar todo esto.

> ⚠️ El ID `com.aferrante.shapester` **no se puede cambiar** después de la primera subida. Si querés otro, avisame antes del paso 2.

---

## 1. Cuenta de Apple Developer (🧑)
1. Entrá a **developer.apple.com/programs/enroll** con tu Apple ID (tiene que tener verificación en dos pasos).
2. Elegí **Individual** (tu nombre aparece como "vendedor" en la App Store).
3. Pagás **USD 99/año**. La aprobación tarda de unas horas a ~2 días.
4. Cuando te llegue el mail de bienvenida → avisame.

## 2. Certificado y perfil, sin Mac (🧑 + 🤖)
1. 🤖 Genero en tu PC una clave privada y un "pedido de certificado" (`.csr`) con OpenSSL.
2. 🧑 En **developer.apple.com → Certificates → +** → *Apple Distribution* → subís el `.csr` → descargás el `.cer`.
3. 🤖 Lo convierto a `.p12` (vos elegís la contraseña y la escribís vos en la terminal).
4. 🧑 **Identifiers → + → App IDs → App** → descripción "Shapester", Bundle ID explícito `com.aferrante.shapester`, sin capacidades extra.
5. 🧑 **Profiles → + → App Store Connect** → elegís ese App ID y el certificado → descargás el `.mobileprovision`.

## 3. Crear la app en App Store Connect (🧑)
**appstoreconnect.apple.com → Apps → + → New App**: plataforma iOS, nombre **Shapester** (si está tomado probamos una variante, p. ej. "Shapester: Shape Packer"), idioma inglés, Bundle ID `com.aferrante.shapester`, SKU `shapester-001`.

## 4. Compilar en la nube de Unity (🧑 + 🤖)
Unity Build Automation compila en Macs de Unity. Necesita el proyecto en un repositorio:
1. 🤖 Preparo el repositorio Git (sin la carpeta `Library`, que es caché).
2. 🧑 Creás un repositorio **privado** en GitHub (o usamos Unity Version Control, que está en el mismo panel de Unity Cloud) y me pasás el link; lo subo.
3. 🧑 En **cloud.unity.com → tu proyecto (en Unity Cloud todavía figura como "Tight Box"; podés renombrarlo en Settings, es solo cosmético) → Build Automation**: conectás el repositorio, creás un target **iOS** con Unity **6000.0.23f1**, y cargás el `.p12` + contraseña + `.mobileprovision`.
4. Se cobra por minuto de compilación en Mac (precio en la página de Build Automation; una build de este juego debería tardar pocos minutos).
5. Resultado: un archivo **`.ipa`**.

## 5. Subir el `.ipa` a Apple (🧑 + 🤖)
Sin Mac hay dos opciones; vemos cuál está disponible en ese momento:
- Que Build Automation lo envíe directo a App Store Connect (con una *API Key* de App Store Connect), o
- **Transporter** de Apple por línea de comandos en Windows (también con la API Key).

## 6. Ficha de la tienda (🤖 preparo · 🧑 cargás)
- 🤖 Descripción, subtítulo y palabras clave en inglés.
- 🤖 Capturas de pantalla para iPhone 6.9" (1320×2868) generadas desde el juego.
- 🤖 Política de privacidad (texto). 🧑 Hay que publicarla en una **URL pública** (Apple la exige).
- 🧑 **App Privacy** en App Store Connect: *Identifiers → User ID* (el login anónimo de Unity), usado para *App Functionality*, **no** vinculado a tu identidad, **sin** tracking.
- 🧑 Clasificación de edad: 4+. Precio: gratis.

## 7. Enviar a revisión (🧑)
- En **notas para el revisor** conviene poner: *"PRACTICE works with one device. Online mode needs 2 devices: one taps PLAY ONLINE → CREATE ROOM, the other PLAY ONLINE → joins the room, the host taps START."*
- La revisión suele tardar **1 a 3 días**. Si rechazan algo, lo arreglamos y se reenvía.
