-- Script do Mini-Ordis para Triagem e Categorizacao Automatica
function analisar_comprovante(texto_bruto)
    local texto = string.lower(texto_bruto)
    local resultado = {
        descricao = "Lancamento Identificado",
        categoria = "Triviais",
        valor = 0.0
    }

    -- 1. Regras de Categorizacao Inteligente (Editaveis a qualquer momento!)
    if string.find(texto, "santa maria") or string.find(texto, "energia") or string.find(texto, "internet") then
        resultado.categoria = "Fixos"
        resultado.descricao = "Fatura Energia / Servicos Casa"
    elseif string.find(texto, "supermercado") or string.find(texto, "atacadao") or string.find(texto, "barilla") then
        resultado.categoria = "Fixos"
        resultado.descricao = "Supermercado e Feira"
    elseif string.find(texto, "posto") or string.find(texto, "combustivel") or string.find(texto, "route 66") or string.find(texto, "bosch") then
        resultado.categoria = "Mobilidade e Manutencao"
        resultado.descricao = "Combustivel / Manutencao Fox"
    elseif string.find(texto, "plus delivery") or string.find(texto, "acai") or string.find(texto, "lanche") then
        resultado.categoria = "Triviais"
        resultado.descricao = "Delivery / Lazer"
    elseif string.find(texto, "steam") or string.find(texto, "luna") or string.find(texto, "jogo") then
        resultado.categoria = "Triviais"
        resultado.descricao = "Jogos e Hobbies"
    end

    -- 2. Cacador de Valor Monetario (Padrao R$ 1.234,56 ou 123,45)
    local match_valor = string.match(texto_bruto, "R%$%s*([%d%.]+,%d%d)") 
                        or string.match(texto_bruto, "([%d%.]+,%d%d)")

    if match_valor then
        -- Converte formato brasileiro (1.250,50) para decimal do Lua (1250.50)
        local limpo = string.gsub(match_valor, "%.", "")
        limpo = string.gsub(limpo, ",", ".")
        resultado.valor = tonumber(limpo) or 0.0
    end

    return resultado
end