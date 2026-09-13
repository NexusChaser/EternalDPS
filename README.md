# EternalDPS

**Eternal Data Persistence System** — sistema de guardado reutilizable para Unity, con un núcleo
que no depende del motor.

> ⚠️ **API inestable.** Este paquete no congela su API pública hasta que exista un **segundo
> proyecto consumidor** real. Hasta entonces, cualquier versión menor puede romper compatibilidad
> de código. El **formato de archivo**, en cambio, sí es contrato desde la versión 1 del
> contenedor: solo se extiende, nunca se cambia.

---

## Qué es

Un guardado tiene tres partes que envejecen distinto: **dónde se escriben los bytes**, **cómo se
convierte un objeto en bytes** y **qué hay dentro**. EternalDPS se queda con las dos primeras y
deja la tercera al juego, que es la única que no se puede reutilizar.

- **Contenedor `.etm`** firmado con HMAC-SHA256, con metadatos legibles sin abrir el cuerpo.
- **Serialización intercambiable** — JSON hoy, binario el día que haga falta, sin romper lo escrito.
- **Almacenes por plataforma** — archivo, WebGL, Steam Cloud, consola. Asíncronos por diseño.
- **Ámbitos y ranuras** — máquina / cuenta / partida, con ranuras opcionales.
- **Herramientas de inspección** sin dependencia del motor, para que cada uno construya su capa visual.

## Estructura

| carpeta | ensamblado | depende de Unity |
|---|---|---|
| `Runtime/Core` | `Eternal.Core` | **no** |
| `Runtime/Tooling` | `Eternal.Tooling` | **no** |
| `Runtime/Serialization.Newtonsoft` | `Eternal.Serialization.Newtonsoft` | **no** |
| `Runtime/Crypto` | `Eternal.Crypto` | **no** |
| `Runtime/Unity` | `Eternal.Unity` | sí |
| `Editor` | `Eternal.Unity.Editor` | sí, solo editor |
| `Tests` | `Eternal.Tests` | sí, solo editor |

Que `Core` y `Tooling` no referencien `UnityEngine` **no es un detalle de estilo**: es lo que
permite ejecutar las pruebas en CI sin abrir Unity, y lo que permitiría usar el núcleo desde otro
motor aportando su propio almacén y su propio serializador.

Los ensamblados opcionales usan **version defines**: si el paquete del que dependen no está
instalado, ni siquiera se compilan. Un proyecto que no quiera Newtonsoft no lo arrastra.

## Instalación

Package Manager → *Add package from git URL*:

```
https://github.com/NexusChaser/EternalDPS.git
```

O en `Packages/manifest.json`:

```json
"com.nexuschaser.eternaldps": "https://github.com/NexusChaser/EternalDPS.git"
```

Para fijar una versión concreta, añade la etiqueta: `...EternalDPS.git#v0.1.0`.

### Mientras se desarrolla el propio paquete

Un paquete instalado por git URL vive en `Library/PackageCache` y es **de solo lectura**. Para
trabajar sobre él, apunta a la copia local:

```json
"com.nexuschaser.eternaldps": "file:../../EternalDPS"
```

La ruta es relativa a la carpeta `Packages/` del proyecto. Se vuelve a la git URL al consumirlo.

## Reglas que no se negocian

Están razonadas en `Documentation~/Architecture.md`. En resumen:

1. **En este repositorio no vive ninguna clave.** Es público. El material lo aporta cada juego a
   través de `IKeyProvider`, desde su propio repositorio privado o desde un secreto de CI. Lo que
   sí pone el paquete es el **generador**: el azar no se improvisa por proyecto.
2. **Nunca se reutiliza un identificador** de transformación, de serializador o de clave.
3. **Los archivos dorados de pruebas no se tocan.** Cada cambio de contenedor añade uno nuevo.
4. **`Core` y `Tooling` no pueden referenciar `UnityEngine`.** Si algún día el CLI deja de
   compilar, es que alguien rompió esta regla.

## Documentación

- `Documentation~/Architecture.md` — qué se construye y por qué.
- `Documentation~/ImplementationPlan.md` — en qué orden y cuándo está hecho.

## Licencia

**Apache License 2.0.** Ver `LICENSE` y `NOTICE`.

Se eligio sobre MIT porque hace exigible lo que el proyecto pide y MIT no:

- **§4(b)** obliga a que todo archivo modificado lleve un aviso visible de que fue cambiado.
- **§4(c)** obliga a conservar los avisos de copyright y atribucion originales.
- **§4(d)** obliga a reproducir el contenido del archivo `NOTICE`.
- **§6** no cede las marcas: nadie puede usar el nombre del proyecto ni el del autor para
  respaldar lo suyo.

Ademas incluye cesion expresa de patentes, cosa que MIT no tiene.

Puedes usarlo y modificarlo en proyectos comerciales y no comerciales. Lo unico que se pide a
cambio es que la atribucion viaje con el codigo y que se declare lo que hayas cambiado.
