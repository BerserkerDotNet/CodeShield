package codeshield.kotlin

import io.github.detekt.parser.KtCompiler
import org.jetbrains.kotlin.com.intellij.psi.PsiElement
import org.jetbrains.kotlin.com.intellij.psi.PsiErrorElement
import org.jetbrains.kotlin.com.intellij.psi.tree.IElementType
import org.jetbrains.kotlin.com.intellij.psi.util.PsiTreeUtil
import org.jetbrains.kotlin.lexer.KotlinLexer
import org.jetbrains.kotlin.lexer.KtTokens
import org.jetbrains.kotlin.psi.KtFile
import java.nio.file.Paths

/** Line and column lookup for one text. Lines and columns are 1-based; `\r\n`, `\r` and `\n` end a line. */
internal class LineIndex(text: String) {
    private val starts: IntArray = lineStarts(text)

    /** 0-based line of an offset. */
    fun lineIndex(offset: Int): Int {
        var low = 0
        var high = starts.size - 1
        while (low < high) {
            val mid = (low + high + 1) ushr 1
            if (starts[mid] <= offset) low = mid else high = mid - 1
        }
        return low
    }

    fun line(offset: Int): Int = lineIndex(offset) + 1

    fun column(offset: Int): Int = offset - starts[lineIndex(offset)] + 1

    fun span(start: Int, end: Int): Span = Span(line(start), column(start), line(end), column(end))

    fun span(element: PsiElement): Span = span(element.textRange.startOffset, element.textRange.endOffset)

    fun line(element: PsiElement): Int = line(element.textRange.startOffset)

    private companion object {
        /** The offset at which each line starts; a `\r\n` pair ends one line. */
        fun lineStarts(text: String): IntArray {
            val list = ArrayList<Int>()
            list.add(0)
            var index = 0
            while (index < text.length) {
                if (text.startsWith("\r\n", index)) index++
                if (text[index] == '\r' || text[index] == '\n') list.add(index + 1)
                index++
            }
            return list.toIntArray()
        }
    }
}

/** One lexer token; offsets index the text that was lexed. */
internal class LexedToken(val type: IElementType, val start: Int, val end: Int)

internal object KotlinSyntax {
    private val trivia = setOf<IElementType>(
        KtTokens.WHITE_SPACE,
        KtTokens.EOL_COMMENT,
        KtTokens.BLOCK_COMMENT,
        KtTokens.DOC_COMMENT,
        KtTokens.SHEBANG_COMMENT,
    )

    /** Every token that is not whitespace or a comment, in source order. Offsets index [text] unchanged. */
    fun codeTokens(text: CharSequence): List<LexedToken> {
        val lexer = KotlinLexer()
        lexer.start(withoutCarriageReturns(text))
        val tokens = ArrayList<LexedToken>()
        while (true) {
            val type = lexer.tokenType ?: break
            if (type !in trivia) tokens.add(LexedToken(type, lexer.tokenStart, lexer.tokenEnd))
            lexer.advance()
        }
        return tokens
    }

    /**
     * The Kotlin lexer only accepts `\n` line breaks (the compiler converts separators before parsing). `\r\n` becomes
     * ` \n` and a lone `\r` becomes `\n`, so every offset still refers to the original text.
     */
    private fun withoutCarriageReturns(text: CharSequence): CharSequence {
        if (text.indexOf('\r') < 0) return text
        return text.toString().replace("\r\n", " \n").replace('\r', '\n')
    }
}

/** A parsed Kotlin file; [text] is the parsed text (line separators normalized to `\n`) and all offsets refer to it. */
internal class ParsedFile(val path: String, val file: KtFile) {
    val text: String = file.text
    val lines = LineIndex(text)

    /** The first syntax error as `line L, column C`, or null when the file parsed cleanly. */
    val syntaxError: String? by lazy {
        PsiTreeUtil.findChildOfType(file, PsiErrorElement::class.java, false)?.let {
            val offset = it.textRange.startOffset
            "line ${lines.line(offset)}, column ${lines.column(offset)}"
        }
    }
}

/** The Kotlin parser of the user's detekt-cli installation. The compiler environment is created once per process. */
internal class KotlinParser(root: String) {
    private val base = Paths.get(root)

    fun parse(path: String, content: String): ParsedFile =
        ParsedFile(path, compiler.createKtFile(content, base, base.resolve(path)))

    private companion object {
        val compiler: KtCompiler by lazy { KtCompiler() }
    }
}
