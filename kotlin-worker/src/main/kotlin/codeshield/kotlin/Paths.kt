package codeshield.kotlin

import java.io.IOException
import java.nio.file.FileSystemException
import java.nio.file.Files
import java.nio.file.InvalidPathException
import java.nio.file.NoSuchFileException
import java.nio.file.NotDirectoryException
import java.nio.file.Paths
import java.security.MessageDigest

internal object RepoPath {
    fun toPosix(path: String): String = path.replace('\\', '/')

    fun dirOf(path: String): String = path.lastIndexOf('/').let { if (it < 0) "" else path.substring(0, it) }

    fun baseName(path: String): String = path.substring(path.lastIndexOf('/') + 1)

    fun join(dir: String, name: String): String = if (dir.isEmpty()) name else "$dir/$name"

    /** True when [path] is [dir] itself or lies below it; the repository root is the empty string. */
    fun isUnder(path: String, dir: String): Boolean = dir.isEmpty() || path == dir || path.startsWith("$dir/")

    /** True for `/x` and for drive-letter paths such as `C:/x`; [posix] is written with forward slashes. */
    fun isAbsolute(posix: String): Boolean = posix.startsWith("/") || (posix.length > 1 && posix[1] == ':')

    /**
     * Resolves `.` and `..` segments of a repository-relative path written with forward slashes. Returns null when the path
     * leaves the repository or is absolute.
     */
    fun normalize(path: String): String? {
        val posix = toPosix(path)
        if (isAbsolute(posix)) return null
        val segments = ArrayList<String>()
        for (segment in posix.split('/')) {
            if (!applySegment(segments, segment)) return null
        }
        return segments.joinToString("/")
    }

    /** Applies one path segment to [segments]; false when `..` would leave the repository. */
    private fun applySegment(segments: MutableList<String>, segment: String): Boolean {
        when (segment) {
            "", "." -> Unit
            ".." -> return segments.removeLastOrNull() != null
            else -> segments.add(segment)
        }
        return true
    }

    fun canonical(path: String): String {
        val resolved = try {
            Paths.get(path).toRealPath().toString()
        } catch (error: IOException) {
            Paths.get(path).toAbsolutePath().normalize().toString()
        } catch (error: InvalidPathException) {
            path
        }
        return toPosix(resolved).trimEnd('/')
    }
}

/** Maps between canonical absolute paths and repository-relative forward-slash paths. */
internal class RepoPaths(root: String) {
    val root: String = RepoPath.canonical(root)
    private val caseInsensitive = System.getProperty("os.name").orEmpty().startsWith("Windows")

    fun toAbs(rel: String): String = "${root}/$rel"

    fun toRel(abs: String): String? {
        val normalized = RepoPath.toPosix(abs)
        val prefix = "$root/"
        if (normalized.length < prefix.length) return null
        return if (normalized.startsWith(prefix, ignoreCase = caseInsensitive)) normalized.substring(prefix.length) else null
    }
}

/** Records every file the analysis reads, hashing the exact bytes that were used. */
internal class InputLedger(private val paths: RepoPaths) {
    private val entries = LinkedHashMap<String, InputRecord>()
    private val failed = HashMap<String, String>()
    private val texts = HashMap<String, String?>()

    /** Paths that exist but could not be read; ordinary absence is not a failure. */
    fun readFailures(): List<Pair<String, String>> = failed.entries.map { it.key to it.value }.sortedBy { it.first }

    /** The UTF-8 text of a repository file (BOM removed), or null when it is absent or unreadable. Each file is read once. */
    fun readRepoText(rel: String): String? {
        if (texts.containsKey(rel)) return texts[rel]
        return readText(paths.toAbs(rel)).also { texts[rel] = it }
    }

    private fun readText(abs: String): String? {
        val bytes = try {
            Files.readAllBytes(Paths.get(abs))
        } catch (error: IOException) {
            noteFailure(abs, error)
            return null
        } catch (error: InvalidPathException) {
            return null
        }
        record(abs, bytes)
        return String(bytes, Charsets.UTF_8).removePrefix("\uFEFF")
    }

    private fun noteFailure(abs: String, error: IOException) {
        if (isAbsence(error)) return
        val real = RepoPath.canonical(abs)
        failed[paths.toRel(real) ?: real] = error.message ?: error.javaClass.simpleName
    }

    /** A missing file or a missing parent directory, which is ordinary absence rather than a read failure. */
    private fun isAbsence(error: IOException): Boolean =
        error is NoSuchFileException || error is NotDirectoryException ||
            (error is FileSystemException && error.reason.orEmpty().contains("Not a directory"))

    private fun record(abs: String, bytes: ByteArray) {
        val real = RepoPath.canonical(abs)
        val rel = paths.toRel(real)
        val scope = if (rel == null) "external" else "repository"
        val recorded = rel ?: real
        entries["$scope|$recorded"] = InputRecord(scope, recorded, sha256(bytes))
    }

    fun snapshot(): List<InputRecord> = entries.values.sortedWith(compareBy({ it.scope }, { it.path }))

    private fun sha256(bytes: ByteArray): String =
        MessageDigest.getInstance("SHA-256").digest(bytes).joinToString("") { String.format("%02x", it.toInt() and 0xFF) }
}

internal fun ordinalSorted(values: Iterable<String>): List<String> = values.toSortedSet().toList()
