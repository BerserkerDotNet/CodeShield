package codeshield.kotlin

import java.util.jar.Manifest

/** Versions of the analyzers on the class path, reported in every analysis so results can be reproduced. */
internal object Versions {
    val detekt: String by lazy { manifestAttribute("DetektVersion") ?: "unknown" }

    /** The Kotlin compiler front-end bundled in detekt-cli; it decides which syntax can be parsed. */
    val kotlin: String by lazy {
        manifestAttribute("KotlinImplementationVersion")
            ?: runCatching { Class.forName("org.jetbrains.kotlin.config.KotlinCompilerVersion").getField("VERSION").get(null) as String }
                .getOrNull()
            ?: KotlinVersion.CURRENT.toString()
    }

    val java: String get() = System.getProperty("java.version").orEmpty()

    fun all(): Map<String, String> = linkedMapOf("detekt" to detekt, "java" to java, "kotlin" to kotlin)

    private fun manifestAttribute(name: String): String? {
        val resources = Versions::class.java.classLoader.getResources("META-INF/MANIFEST.MF")
        while (resources.hasMoreElements()) {
            val value = runCatching { resources.nextElement().openStream().use { Manifest(it).mainAttributes.getValue(name) } }.getOrNull()
            if (!value.isNullOrBlank()) return value.trim()
        }
        return null
    }
}
