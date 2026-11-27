package codeshield.kotlin

import org.junit.jupiter.api.Assertions.assertEquals
import org.junit.jupiter.api.Assertions.assertNull
import org.junit.jupiter.api.Assertions.assertTrue
import org.junit.jupiter.api.Test
import org.junit.jupiter.api.io.TempDir
import java.nio.file.Path

/** Gradle builds are read statically: projects, source ownership, module edges and the scripts that select contexts. */
class GradleTest {
    @Test
    fun `a Kotlin DSL multi-project build has one module per build script and edges for project dependencies`() {
        val analysis = kotlinDslBuild

        assertEquals(
            listOf("app/build.gradle.kts|main", "core-data/build.gradle.kts|main", "core/build.gradle.kts|main", "core/build.gradle.kts|test"),
            analysis.contextIds,
            "the root project owns no source, so its context is not selected",
        )
        assertEquals(
            listOf("app/build.gradle.kts", "build.gradle.kts", "core-data/build.gradle.kts", "core/build.gradle.kts"),
            analysis.graph["modules"],
        )
        val edges = listOf("app/build.gradle.kts" to "core-data/build.gradle.kts", "app/build.gradle.kts" to "core/build.gradle.kts")
        assertEquals(edges.map { edge(it) }, analysis.graph["edges"])
        assertEquals(listOf(linkedMapOf("contextId" to "main", "edges" to edges.map { edge(it) }, "fileImports" to emptyList<Any>(), "support" to emptyList<Any>())),
            analysis.graph["contexts"])
        assertEquals(emptyList<Any>(), analysis.graph["fileImports"])
    }

    @Test
    fun `a Kotlin DSL build reports project dependencies it cannot resolve or attribute to one project`() {
        val unresolved = Analysis.list(kotlinDslBuild.graph, "unresolved")

        assertEquals(
            listOf(
                reason(ReasonCodes.MISSING_DEPENDENCY, "Cannot resolve project accessor 'projects.missing' referenced by app/build.gradle.kts at line 5.", "app/build.gradle.kts"),
                reason(
                    ReasonCodes.MISSING_DEPENDENCY,
                    "Cannot attribute the project dependency in build.gradle.kts at line 2 to a single project; " +
                        "dependencies declared for several projects at once are not modelled.",
                    "build.gradle.kts",
                ),
            ),
            unresolved,
            "test-scoped project dependencies are not module edges, so ':testing' is not resolved",
        )
    }

    @Test
    fun `the main and test sources of a Kotlin DSL project are separate contexts, and only the main sources are a module`() {
        val analysis = kotlinDslBuild

        val main = analysis.context("core/build.gradle.kts|main")
        assertEquals("kotlin", main["language"])
        assertEquals("core/build.gradle.kts", main["configPath"])
        assertEquals("core/build.gradle.kts", main["modulePath"])
        assertNull(main["framework"])
        assertEquals(listOf("core/src/main/kotlin/core/Repository.kt"), main["ownedFiles"])
        val test = analysis.context("core/build.gradle.kts|test")
        assertEquals("core/build.gradle.kts", test["configPath"])
        assertNull(test["modulePath"], "test sources are not a module of their own")
        assertEquals(listOf("core/src/test/kotlin/core/RepositoryTest.kt"), test["ownedFiles"])
        assertEquals(emptyList<Any>(), analysis.unownedFiles)
    }

    @Test
    fun `a Groovy Android build owns conventional and declared source directories`() {
        val analysis = groovyAndroidBuild

        assertEquals(
            listOf("android/build.gradle|main", "android/build.gradle|test", "feature/login/build.gradle|main", "lib/build.gradle|main"),
            analysis.contextIds,
        )
        assertEquals(
            listOf("android/generated/extra/Extra.kt", "android/src/debug/kotlin/com/x/DebugTools.kt", "android/src/main/java/com/x/MainActivity.kt"),
            analysis.context("android/build.gradle|main")["ownedFiles"],
        )
        assertEquals(
            listOf("android/spec/SpecTest.kt", "android/src/androidTest/kotlin/com/x/MainTest.kt"),
            analysis.context("android/build.gradle|test")["ownedFiles"],
        )
        assertEquals(listOf("stray/Orphan.kt"), analysis.unownedFiles)
    }

    @Test
    fun `a Groovy build has one module per build script and edges for project dependencies`() {
        val analysis = groovyAndroidBuild

        assertEquals(
            listOf("android/build.gradle", "build.gradle", "feature/login/build.gradle", "lib/build.gradle"),
            analysis.graph["modules"],
        )
        assertEquals(
            listOf(
                edge("android/build.gradle" to "feature/login/build.gradle"),
                edge("android/build.gradle" to "lib/build.gradle"),
                edge("feature/login/build.gradle" to "lib/build.gradle"),
            ),
            analysis.graph["edges"],
        )
        assertEquals(emptyList<Any>(), analysis.graph["unresolved"])
    }

    @Test
    fun `Kotlin Multiplatform source sets belong to one project`() {
        val analysis = multiplatformBuild

        assertEquals(
            listOf("androidApp/build.gradle.kts|main", "core/build.gradle.kts|main", "shared/build.gradle.kts|main", "shared/build.gradle.kts|test"),
            analysis.contextIds,
        )
        assertEquals(
            listOf(
                "shared/src/androidMain/kotlin/Platform.android.kt",
                "shared/src/commonMain/kotlin/Platform.kt",
                "shared/src/iosMain/kotlin/Platform.ios.kt",
            ),
            analysis.context("shared/build.gradle.kts|main")["ownedFiles"],
        )
        assertEquals(listOf("shared/src/commonTest/kotlin/PlatformTest.kt"), analysis.context("shared/build.gradle.kts|test")["ownedFiles"])
        assertEquals(
            listOf(edge("androidApp/build.gradle.kts" to "shared/build.gradle.kts"), edge("shared/build.gradle.kts" to "core/build.gradle.kts")),
            analysis.graph["edges"],
        )
        assertEquals(emptyList<Any>(), analysis.graph["unresolved"])
    }

    @Test
    fun `Kotlin Multiplatform expect declarations stand for their actuals`() {
        val platform = Analysis.list(multiplatformBuild.context("shared/build.gradle.kts|main"), "types")

        assertEquals(
            listOf(linkedMapOf("unitId" to "shared/src/commonMain/kotlin/Platform.kt#Platform", "file" to "shared/src/commonMain/kotlin/Platform.kt", "isAbstract" to false)),
            platform,
        )
    }

    @Test
    fun `a changed script selects the contexts it configures, including scripts applied with apply from`(@TempDir root: Path) {
        val repo = Repo(root)
            .file("settings.gradle.kts", """include(":app", ":lib")""")
            .file("app/build.gradle.kts", """apply(from = "../gradle/common.gradle.kts")""")
            .file("lib/build.gradle.kts", """apply(from = "missing.gradle.kts")""")
            .file("gradle/common.gradle.kts", "")
            .file("gradle/other.gradle.kts", "")
            .file("app/src/main/kotlin/A.kt", "class A")
            .file("app/src/test/kotlin/ATest.kt", "class ATest")
            .file("lib/src/main/kotlin/L.kt", "class L")

        assertEquals(listOf("app/build.gradle.kts|main", "app/build.gradle.kts|test"), repo.analyze(focus = emptyList(), changedConfigFiles = listOf("gradle/common.gradle.kts")).contextIds)
        assertEquals(listOf("lib/build.gradle.kts|main"), repo.analyze(focus = emptyList(), changedConfigFiles = listOf("lib/build.gradle.kts")).contextIds)
        assertEquals(
            listOf("app/build.gradle.kts|main", "app/build.gradle.kts|test", "lib/build.gradle.kts|main"),
            repo.analyze(focus = emptyList(), changedConfigFiles = listOf("settings.gradle.kts")).contextIds,
        )
        assertEquals(emptyList<Any>(), repo.analyze(focus = emptyList(), changedConfigFiles = listOf("gradle/other.gradle.kts")).contextIds)
        val focused = repo.analyze(focus = listOf("app/src/test/kotlin/ATest.kt"))
        assertEquals(listOf("app/build.gradle.kts|test"), focused.contextIds)
        assertEquals(
            listOf(reason(ReasonCodes.MISSING_DEPENDENCY, "Cannot read script 'lib/missing.gradle.kts' applied by lib/build.gradle.kts at line 1.", "lib/build.gradle.kts")),
            focused.graph["unresolved"],
        )
    }

    @Test
    fun `a project without a build script is anchored on the settings file and is not a module path`(@TempDir root: Path) {
        val analysis = Repo(root)
            .file("settings.gradle.kts", """include(":lib")""")
            .file("lib/src/main/kotlin/L.kt", "class L")
            .analyze()

        val context = analysis.context("settings.gradle.kts|:lib|main")
        assertEquals(listOf("settings.gradle.kts|:lib|main"), analysis.contextIds)
        assertEquals("settings.gradle.kts", context["configPath"])
        assertNull(context["modulePath"])
        assertEquals(listOf("lib"), analysis.graph["modules"])
    }

    @Test
    fun `a build script outside every settings file is a single-project build`(@TempDir root: Path) {
        val analysis = Repo(root)
            .file("build.gradle.kts", "")
            .file("src/main/kotlin/A.kt", "class A")
            .file("src/test/kotlin/ATest.kt", "class ATest")
            .analyze()

        assertEquals(listOf("build.gradle.kts|main", "build.gradle.kts|test"), analysis.contextIds)
        assertEquals(listOf("build.gradle.kts"), analysis.graph["modules"])
    }

    @Test
    fun `a Kotlin syntax error makes the source unreadable for its context`(@TempDir root: Path) {
        val analysis = Repo(root)
            .file("build.gradle.kts", "")
            .file("src/main/kotlin/Broken.kt", "fun broken( {")
            .analyze()

        val failures = analysis.failures()
        assertEquals(1, failures.size, "failures = $failures")
        assertEquals(ReasonCodes.UNREADABLE_SOURCE, failures[0]["code"])
        assertTrue((failures[0]["message"] as String).startsWith("Cannot parse src/main/kotlin/Broken.kt: syntax error at line 1, column "))
        assertEquals("build.gradle.kts|main", failures[0]["contextId"])
    }

    @Test
    fun `a build script that cannot be parsed fails its project`(@TempDir root: Path) {
        val analysis = Repo(root)
            .file("settings.gradle.kts", """include(":app")""")
            .file("app/build.gradle.kts", """dependencies { implementation(project(":core") }""")
            .file("app/src/main/kotlin/A.kt", "class A")
            .analyze()

        val failure = analysis.context("app/build.gradle.kts|main").let { Analysis.list(it, "failures") }.single()
        assertEquals(ReasonCodes.PROJECT_LOAD_FAILED, failure["code"])
        assertTrue((failure["message"] as String).startsWith("Cannot parse app/build.gradle.kts: syntax error at line 1, column "), "$failure")
        assertEquals("app/build.gradle.kts", failure["contextId"])
        assertEquals(listOf(failure), analysis.graph["unresolved"])
    }

    private fun edge(pair: Pair<String, String>): Map<String, Any> = linkedMapOf("from" to pair.first, "to" to pair.second, "typeOnly" to false)

    private fun reason(code: String, message: String, contextId: String): Map<String, Any?> =
        linkedMapOf("code" to code, "message" to message, "ruleId" to null, "contextId" to contextId)

    private companion object {
        val kotlinDslBuild: Analysis by lazy {
            analyzeOnce("codeshield-gradle-kts") {
                file("settings.gradle.kts", """
                    rootProject.name = "shop"
                    include(":app", ":core", ":core-data")
                """)
                file("build.gradle.kts", """
                    subprojects {
                        dependencies { implementation(project(":core")) }
                    }
                """)
                file("app/build.gradle.kts", """
                    plugins { kotlin("jvm") }
                    dependencies {
                        implementation(project(":core"))
                        implementation(projects.coreData)
                        implementation(projects.missing)
                        testImplementation(project(":testing"))
                    }
                """)
                file("core/build.gradle.kts", """plugins { kotlin("jvm") }""")
                file("core-data/build.gradle.kts", "")
                file("app/src/main/kotlin/app/Main.kt", "fun main() = Unit")
                file("core/src/main/kotlin/core/Repository.kt", "class Repository")
                file("core/src/test/kotlin/core/RepositoryTest.kt", "class RepositoryTest")
                file("core-data/src/main/kotlin/Data.kt", "class Data")
            }
        }

        val groovyAndroidBuild: Analysis by lazy {
            analyzeOnce("codeshield-gradle-groovy") {
                file("settings.gradle", """
                    rootProject.name = 'groovy'
                    include ':android', ':lib'
                    include 'feature:login'
                """)
                file("build.gradle", "buildscript { repositories { google() } }")
                file("android/build.gradle", """
                    apply plugin: 'com.android.application'
                    android {
                        sourceSets {
                            main.java.srcDirs += 'generated/extra'
                            test.java.srcDir 'spec'
                        }
                    }
                    dependencies {
                        implementation project(':lib')
                        implementation project(path: ':feature:login')
                        androidTestImplementation project(':missing')
                    }
                """)
                file("feature/login/build.gradle", """
                    plugins { id 'com.android.library' }
                    dependencies { api project(":lib") }
                """)
                file("lib/build.gradle", "plugins { id 'org.jetbrains.kotlin.jvm' }")
                file("android/src/main/java/com/x/MainActivity.kt", "class MainActivity")
                file("android/src/debug/kotlin/com/x/DebugTools.kt", "object DebugTools")
                file("android/generated/extra/Extra.kt", "class Extra")
                file("android/src/androidTest/kotlin/com/x/MainTest.kt", "class MainTest")
                file("android/spec/SpecTest.kt", "class SpecTest")
                file("feature/login/src/main/kotlin/login/Login.kt", "class Login")
                file("lib/src/main/kotlin/lib/Util.kt", "object Util")
                file("stray/Orphan.kt", "class Orphan")
            }
        }

        val multiplatformBuild: Analysis by lazy {
            analyzeOnce("codeshield-gradle-kmp") {
                file("settings.gradle.kts", """
                    rootProject.name = "kmp"
                    include(":shared", ":core", ":androidApp")
                """)
                file("shared/build.gradle.kts", """
                    plugins { kotlin("multiplatform") }
                    kotlin {
                        androidTarget()
                        iosArm64()
                        sourceSets {
                            commonMain.dependencies { implementation(projects.core) }
                            commonTest.dependencies { implementation(project(":testing")) }
                        }
                    }
                """)
                file("core/build.gradle.kts", """plugins { kotlin("multiplatform") }""")
                file("androidApp/build.gradle.kts", """dependencies { implementation(project(":shared")) }""")
                file("shared/src/commonMain/kotlin/Platform.kt", "expect class Platform() { val name: String }")
                file("shared/src/androidMain/kotlin/Platform.android.kt", "actual class Platform actual constructor() { actual val name: String = \"android\" }")
                file("shared/src/iosMain/kotlin/Platform.ios.kt", "actual class Platform actual constructor() { actual val name: String = \"ios\" }")
                file("shared/src/commonTest/kotlin/PlatformTest.kt", "class PlatformTest")
                file("core/src/commonMain/kotlin/Core.kt", "class Core")
                file("androidApp/src/main/kotlin/App.kt", "class App")
            }
        }
    }
}
