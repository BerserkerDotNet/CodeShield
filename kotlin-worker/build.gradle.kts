import org.jetbrains.kotlin.gradle.dsl.JvmTarget
import org.jetbrains.kotlin.gradle.dsl.KotlinVersion

plugins {
    kotlin("jvm") version "2.0.21"
}

// The worker runs with the user's detekt-cli 1.23.x on the class path; it supplies the Kotlin runtime, the Kotlin compiler
// front-end (PSI) and the detekt rules. Compiling against the oldest supported release keeps the worker binary-compatible
// with every 1.23.x, and only CodeShield's own classes go into the jar.
val oldestDetekt = "1.23.0"
val oldestKotlin = "1.8.21"
val testDetekt = providers.gradleProperty("detektVersion").get()

repositories {
    mavenCentral()
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

kotlin {
    compilerOptions {
        jvmTarget.set(JvmTarget.JVM_17)
        apiVersion.set(KotlinVersion.KOTLIN_1_8)
        languageVersion.set(KotlinVersion.KOTLIN_1_8)
        freeCompilerArgs.add("-Xsuppress-version-warnings")
    }
}

configurations.testCompileOnly {
    extendsFrom(configurations.compileOnly.get())
}

dependencies {
    compileOnly("org.jetbrains.kotlin:kotlin-stdlib:$oldestKotlin")
    compileOnly("org.jetbrains.kotlin:kotlin-compiler-embeddable:$oldestKotlin")
    compileOnly("io.gitlab.arturbosch.detekt:detekt-api:$oldestDetekt")
    compileOnly("io.gitlab.arturbosch.detekt:detekt-parser:$oldestDetekt")

    testImplementation(platform("org.junit:junit-bom:5.11.4"))
    testImplementation("org.junit.jupiter:junit-jupiter")
    testRuntimeOnly("org.junit.platform:junit-platform-launcher")
    testRuntimeOnly("io.gitlab.arturbosch.detekt:detekt-cli:$testDetekt:all") {
        isTransitive = false
    }
}

tasks.withType<JavaCompile>().configureEach {
    options.release.set(17)
}

tasks.jar {
    archiveFileName.set("codeshield-kotlin-worker.jar")
    destinationDirectory.set(layout.projectDirectory.dir("dist"))
    isPreserveFileTimestamps = false
    isReproducibleFileOrder = true
    manifest {
        attributes("Implementation-Title" to "CodeShield Kotlin worker")
    }
}

tasks.clean {
    delete(layout.projectDirectory.dir("dist"))
}

tasks.test {
    useJUnitPlatform()
    systemProperty("codeshield.detektVersion", testDetekt)
    jvmArgs("-Xss16m", "-Djava.awt.headless=true")
    testLogging {
        events("failed")
        exceptionFormat = org.gradle.api.tasks.testing.logging.TestExceptionFormat.FULL
    }
}
