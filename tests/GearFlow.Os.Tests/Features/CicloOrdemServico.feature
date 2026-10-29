# language: pt
Funcionalidade: Ciclo local da ordem de serviço

  Cenário: Abrir a OS, atualizar o status e consultar o histórico
    Dado um cliente com um veículo cadastrado
    Quando a recepcionista abre uma OS com o serviço "Troca de óleo"
    Então o status da OS é "Created"
    E a consulta devolve o histórico com "Ordem de serviço aberta."
    Quando chega o evento OrcamentoGerado
    Então o status da OS é "WaitingOrcamento"
    Quando chega o evento OrcamentoApproved
    Então o status da OS é "InProgress"
    E o histórico contém o status "Paid"
    Quando chega o evento OperationCompleted
    E a recepcionista registra a entrega
    Então o status da OS é "Completed"
    E o histórico contém o status "Completed"
